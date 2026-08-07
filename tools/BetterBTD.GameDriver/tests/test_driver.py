import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from betterbtd_game_driver.driver import (
    _validate_window_after_capture,
    _write_evidence,
    resolve_output_path,
)
from betterbtd_game_driver.errors import GameDriverError, UsageError
from betterbtd_game_driver.models import Rect, WindowSnapshot


class DriverPathTests(unittest.TestCase):
    def test_explicit_output_path_is_resolved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            requested = Path(temporary_directory) / "frame.png"

            actual = resolve_output_path(
                requested,
                123,
                datetime(2026, 8, 7, tzinfo=timezone.utc),
            )

            self.assertEqual(requested.resolve(), actual)

    def test_default_output_uses_ignored_artifact_directory(self) -> None:
        actual = resolve_output_path(
            None,
            123,
            datetime(2026, 8, 7, 12, 34, 56, 789000, tzinfo=timezone.utc),
        )

        self.assertEqual(".png", actual.suffix)
        self.assertIn(str(Path("artifacts") / "game-driver" / "20260807"), str(actual))
        self.assertIn("123", actual.name)

    def test_non_png_explicit_output_is_rejected(self) -> None:
        with self.assertRaisesRegex(UsageError, "must name a .png"):
            resolve_output_path(
                Path("frame.bmp"),
                123,
                datetime(2026, 8, 7, tzinfo=timezone.utc),
            )

    def test_evidence_completion_marker_commits_matching_pair(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            image_path = root / "frame.png"
            metadata_path = root / "frame.json"
            completion_path = root / "frame.complete.json"
            lock_path = root / "frame.lock"
            image_bytes = b"png evidence"
            evidence = {"schemaVersion": 1, "evidenceId": "evidence-1"}

            _write_evidence(
                image_path,
                metadata_path,
                completion_path,
                lock_path,
                image_bytes,
                evidence,
                "evidence-1",
                overwrite=False,
            )

            completion = json.loads(completion_path.read_text(encoding="utf-8"))
            self.assertEqual("evidence-1", completion["evidenceId"])
            self.assertEqual(hashlib.sha256(image_bytes).hexdigest(), completion["imageSha256"])
            self.assertEqual(
                hashlib.sha256(metadata_path.read_bytes()).hexdigest(),
                completion["metadataSha256"],
            )
            self.assertFalse(lock_path.exists())

    def test_failed_overwrite_removes_old_completion_marker(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            image_path = root / "frame.png"
            metadata_path = root / "frame.json"
            completion_path = root / "frame.complete.json"
            lock_path = root / "frame.lock"
            image_path.write_bytes(b"old image")
            metadata_path.write_text("old metadata", encoding="utf-8")
            completion_path.write_text("old completion", encoding="utf-8")

            with patch(
                "betterbtd_game_driver.driver.os.replace",
                side_effect=[None, OSError("simulated metadata replace failure")],
            ):
                with self.assertRaisesRegex(OSError, "simulated metadata replace failure"):
                    _write_evidence(
                        image_path,
                        metadata_path,
                        completion_path,
                        lock_path,
                        b"new image",
                        {"schemaVersion": 1, "evidenceId": "evidence-2"},
                        "evidence-2",
                        overwrite=True,
                    )

            self.assertFalse(completion_path.exists())
            self.assertFalse(lock_path.exists())

    def test_changed_client_rect_invalidates_capture(self) -> None:
        before = _window_snapshot(client_rect=Rect(100, 200, 1920, 1080))
        after = _window_snapshot(client_rect=Rect(101, 200, 1920, 1080))

        with self.assertRaises(GameDriverError) as context:
            _validate_window_after_capture(before, after, require_foreground=True)

        self.assertEqual("windowChangedDuringCapture", context.exception.code)
        self.assertIn("clientRect", context.exception.message)

    def test_stable_window_remains_valid(self) -> None:
        snapshot = _window_snapshot(client_rect=Rect(100, 200, 1920, 1080))

        _validate_window_after_capture(snapshot, snapshot, require_foreground=True)


def _window_snapshot(*, client_rect: Rect) -> WindowSnapshot:
    return WindowSnapshot(
        handle=123,
        process_id=456,
        process_name="BloonsTD6",
        title="BloonsTD6",
        visible=True,
        minimized=False,
        foreground=True,
        dpi=192,
        window_rect=Rect(90, 150, 1940, 1150),
        client_rect=client_rect,
    )


if __name__ == "__main__":
    unittest.main()
