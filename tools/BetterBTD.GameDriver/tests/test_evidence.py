from pathlib import Path
import tempfile
import unittest
from typing import Callable

from PIL import Image

from betterbtd_game_driver.evidence import read_evidence
from betterbtd_game_driver.errors import GameDriverError
from visual_test_support import write_test_evidence


class EvidenceReaderTests(unittest.TestCase):
    def test_valid_bundle_uses_adjacent_files_and_is_oracle_eligible(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            metadata_path = write_test_evidence(
                root,
                "frame",
                Image.new("RGB", (16, 9), (10, 20, 30)),
            )

            evidence = read_evidence(metadata_path)

            self.assertEqual(root / "frame.png", evidence.image_path)
            self.assertEqual("test-frame", evidence.evidence_id)
            self.assertTrue(evidence.oracle_eligible)

    def test_capture_warning_makes_evidence_ineligible_for_oracle_use(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "warning",
                Image.new("RGB", (16, 9)),
                warnings=[{"code": "foregroundNotVerified", "message": "test"}],
            )

            evidence = read_evidence(metadata_path)

            self.assertFalse(evidence.oracle_eligible)

    def test_modified_image_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            metadata_path = write_test_evidence(
                root,
                "modified",
                Image.new("RGB", (16, 9)),
            )
            (root / "modified.png").write_bytes(b"changed")

            with self.assertRaises(GameDriverError) as context:
                read_evidence(metadata_path)

            self.assertEqual("evidenceIntegrityFailed", context.exception.code)

    def test_missing_completion_marker_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            metadata_path = write_test_evidence(
                root,
                "incomplete",
                Image.new("RGB", (16, 9)),
            )
            (root / "incomplete.complete.json").unlink()

            with self.assertRaises(GameDriverError) as context:
                read_evidence(metadata_path)

            self.assertEqual("evidenceIncomplete", context.exception.code)

    def test_non_driver_capture_backend_is_not_oracle_eligible(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "backend",
                Image.new("RGB", (16, 9)),
            )
            _rewrite_metadata(
                metadata_path,
                lambda document: document["capture"].update({"backend": "test"}),
            )

            evidence = read_evidence(metadata_path)

            self.assertFalse(evidence.oracle_eligible)

    def test_foreground_lost_after_capture_is_not_oracle_eligible(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "foreground",
                Image.new("RGB", (16, 9)),
            )
            _rewrite_metadata(
                metadata_path,
                lambda document: document["windowAfterCapture"].update(
                    {"foreground": False}
                ),
            )

            evidence = read_evidence(metadata_path)

            self.assertFalse(evidence.oracle_eligible)

    def test_malformed_warning_is_rejected_instead_of_filtered_out(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "warnings",
                Image.new("RGB", (16, 9)),
            )
            _rewrite_metadata(
                metadata_path,
                lambda document: document.update({"warnings": ["invalid"]}),
            )

            with self.assertRaises(GameDriverError) as context:
                read_evidence(metadata_path)

            self.assertEqual("evidenceIntegrityFailed", context.exception.code)


def _rewrite_metadata(
    metadata_path: Path,
    update: Callable[[dict[str, object]], None],
) -> None:
    import hashlib
    import json
    import os

    document = json.loads(metadata_path.read_text(encoding="utf-8"))
    update(document)
    metadata_bytes = (json.dumps(document, indent=2) + os.linesep).encode("utf-8")
    metadata_path.write_bytes(metadata_bytes)
    completion_path = metadata_path.with_name(f"{metadata_path.stem}.complete.json")
    completion = json.loads(completion_path.read_text(encoding="utf-8"))
    completion["metadataSha256"] = hashlib.sha256(metadata_bytes).hexdigest()
    completion_path.write_text(
        json.dumps(completion, indent=2) + os.linesep,
        encoding="utf-8",
    )


if __name__ == "__main__":
    unittest.main()
