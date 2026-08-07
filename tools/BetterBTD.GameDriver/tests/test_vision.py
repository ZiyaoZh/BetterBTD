from pathlib import Path
import tempfile
import unittest
from dataclasses import replace

from PIL import Image

from betterbtd_game_driver.evidence import read_evidence
from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.vision import recognize_image, write_annotation
from betterbtd_game_driver.visual_catalog import (
    DEFAULT_CATALOG_PATH,
    VisualCatalog,
    VisualPage,
    load_visual_catalog,
)
from visual_test_support import write_test_evidence


SAMPLE_ROOT = DEFAULT_CATALOG_PATH.parent / "samples"


class VisualRecognitionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog()

    def test_real_holdout_frame_matches_main_menu(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("mainMenu", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(19, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        start = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "mainMenu.start"
        )
        player = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "mainMenu.playerSummary"
        )
        self.assertEqual("visible", start["visibility"])
        self.assertTrue(start["visible"])
        self.assertEqual("notEvaluated", player["visibility"])
        self.assertIsNone(player["visible"])

    def test_real_loading_frame_is_unknown(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "loading.unknown.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNone(match)
        self.assertEqual("unknown", result["recognition"]["status"])
        self.assertFalse(result["recognition"]["oracleEligible"])

    def test_uniform_dark_frame_is_unknown(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "dark",
                Image.new("RGB", (1920, 1080), (0, 0, 0)),
            )
            evidence = read_evidence(metadata_path)

            result, match = recognize_image(evidence, self.catalog)

            self.assertIsNone(match)
            self.assertEqual("unknown", result["recognition"]["status"])
            self.assertEqual([], result["recognition"]["elements"])
            self.assertFalse(result["recognition"]["oracleEligible"])

    def test_close_runner_up_below_its_threshold_is_ambiguous(self) -> None:
        source_page = self.catalog.pages[0]
        second_page = VisualPage(
            id="duplicatePage",
            kind="page",
            minimum_score=1.0,
            minimum_matched_anchors=source_page.minimum_matched_anchors,
            anchors=source_page.anchors,
            elements=(),
        )
        catalog = VisualCatalog(
            id="ambiguous-test",
            version=1,
            path=self.catalog.path,
            reference_width=self.catalog.reference_width,
            reference_height=self.catalog.reference_height,
            pages=(source_page, second_page),
            sha256=self.catalog.sha256,
        )
        evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")

        result, match = recognize_image(evidence, catalog)

        self.assertIsNone(match)
        self.assertEqual("ambiguous", result["recognition"]["status"])
        self.assertFalse(result["recognition"]["oracleEligible"])

    def test_unchecked_element_occlusion_is_not_reported_as_visible(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(SAMPLE_ROOT / "main-menu.zh-CN.holdout.png") as source:
                modified = source.convert("RGB")
                modified.paste((0, 0, 0), (1710, 850, 1920, 1080))
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "occluded-mods",
                modified,
            )
            evidence = read_evidence(metadata_path)

            result, _ = recognize_image(evidence, self.catalog)

            self.assertEqual("matched", result["recognition"]["status"])
            mods = next(
                element
                for element in result["recognition"]["elements"]
                if element["id"] == "mainMenu.mods"
            )
            self.assertEqual("notEvaluated", mods["visibility"])
            self.assertIsNone(mods["visible"])

    def test_recognition_uses_image_bytes_verified_at_evidence_read_time(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            with Image.open(SAMPLE_ROOT / "main-menu.zh-CN.holdout.png") as source:
                metadata_path = write_test_evidence(root, "snapshot", source.convert("RGB"))
            evidence = read_evidence(metadata_path)
            evidence.image_path.write_bytes(b"replaced after verification")

            result, _ = recognize_image(evidence, self.catalog)

            self.assertEqual("matched", result["recognition"]["status"])
            self.assertEqual(evidence.image_sha256, result["evidence"]["image"]["sha256"])

    def test_annotation_cannot_replace_source_evidence(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")
        _, match = recognize_image(evidence, self.catalog)
        before = evidence.image_path.read_bytes()

        with self.assertRaises(GameDriverError) as context:
            write_annotation(
                evidence.image_path,
                evidence.image_path,
                match,
                overwrite=True,
            )

        self.assertEqual("protectedEvidenceOutput", context.exception.code)
        self.assertEqual(before, evidence.image_path.read_bytes())

    def test_scaled_16_by_9_frame_matches_and_scales_element_coordinates(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(SAMPLE_ROOT / "main-menu.zh-CN.holdout.png") as source:
                scaled = source.convert("RGB").resize((1280, 720), Image.Resampling.LANCZOS)
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "scaled",
                scaled,
            )
            evidence = read_evidence(metadata_path)

            result, _ = recognize_image(evidence, self.catalog)

            self.assertEqual("matched", result["recognition"]["status"])
            start = next(
                element
                for element in result["recognition"]["elements"]
                if element["id"] == "mainMenu.start"
            )
            self.assertEqual({"x": 640, "y": 633}, start["actionPointClient"])

    def test_non_16_by_9_frame_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "wrong-aspect",
                Image.new("RGB", (1000, 700)),
                has_reference_aspect_ratio=False,
            )
            evidence = read_evidence(metadata_path)

            with self.assertRaises(GameDriverError) as context:
                recognize_image(evidence, self.catalog)

            self.assertEqual("unsupportedObservationAspectRatio", context.exception.code)

    def test_image_size_must_match_evidence_geometry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "geometry",
                Image.new("RGB", (1920, 1080)),
            )
            evidence = read_evidence(metadata_path)
            evidence.metadata["coordinateSystem"]["bounds"]["xMaximumExclusive"] = 1280

            with self.assertRaises(GameDriverError) as context:
                recognize_image(evidence, self.catalog)

            self.assertEqual("evidenceGeometryMismatch", context.exception.code)


if __name__ == "__main__":
    unittest.main()
