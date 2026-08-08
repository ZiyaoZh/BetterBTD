import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.visual_catalog import load_visual_catalog


class VisualCatalogTests(unittest.TestCase):
    def test_bundled_catalog_and_template_hashes_are_valid(self) -> None:
        catalog = load_visual_catalog()

        self.assertEqual("btd6-ui-independent", catalog.id)
        self.assertEqual((1920, 1080), (catalog.reference_width, catalog.reference_height))
        self.assertEqual(
            [
                "welcome",
                "modifiedClientWarning",
                "mainMenu",
                "mapSelect",
                "difficultySelect",
                "easyModeSelect",
            ],
            [page.id for page in catalog.pages],
        )
        self.assertEqual(4, len(catalog.pages[0].anchors))
        self.assertEqual(5, len(catalog.pages[1].anchors))
        self.assertEqual(4, len(catalog.pages[2].anchors))
        self.assertEqual(5, len(catalog.pages[3].anchors))
        self.assertEqual(4, len(catalog.pages[4].anchors))
        self.assertEqual(4, len(catalog.pages[5].anchors))
        self.assertTrue(all(page.positive_holdout is not None for page in catalog.pages))

    def test_positive_holdout_must_differ_from_template_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            anchor = document["pages"][0]["anchors"][0]
            document["pages"][0]["positiveHoldout"] = {
                "evidence": anchor["sourceEvidence"],
                "evidenceId": anchor["sourceEvidenceId"],
                "imageSha256": anchor["sourceImageSha256"],
            }
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertEqual("visualCatalogInvalid", context.exception.code)
            self.assertIn("must differ", context.exception.message)

    def test_template_hash_mismatch_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            document["pages"][0]["anchors"][0]["templateSha256"] = "0" * 64
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertEqual("visualCatalogInvalid", context.exception.code)
            self.assertIn("template hash mismatch", context.exception.message)

    def test_template_path_cannot_escape_catalog_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            document["pages"][0]["anchors"][0]["template"] = "../outside.png"
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertIn("escapes the catalog directory", context.exception.message)

    def test_out_of_bounds_element_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            document["pages"][0]["elements"][0]["bounds"]["x"] = 1919
            document["pages"][0]["elements"][0]["bounds"]["width"] = 2
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertIn("bounds must be inside", context.exception.message)

    def test_template_cannot_overlap_source_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            source_path = root / "source.png"
            anchor = document["pages"][0]["anchors"][0]
            anchor["template"] = "source.png"
            anchor["templateSha256"] = hashlib.sha256(source_path.read_bytes()).hexdigest()
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertIn("overlap protected source evidence", context.exception.message)


def _write_minimal_catalog(root: Path) -> Path:
    template_path = root / "template.png"
    template_path.write_bytes(b"template")
    template_hash = hashlib.sha256(template_path.read_bytes()).hexdigest()
    from PIL import Image
    from visual_test_support import write_test_evidence

    source_path = write_test_evidence(
        root,
        "source",
        Image.new("RGB", (1920, 1080)),
    )
    source_document = json.loads(source_path.read_text(encoding="utf-8"))
    source_hash = hashlib.sha256((root / "source.png").read_bytes()).hexdigest()
    holdout_path = write_test_evidence(
        root,
        "holdout",
        Image.new("RGB", (1920, 1080), "white"),
    )
    holdout_document = json.loads(holdout_path.read_text(encoding="utf-8"))
    holdout_hash = hashlib.sha256((root / "holdout.png").read_bytes()).hexdigest()
    document = {
        "schemaVersion": 1,
        "catalogId": "test",
        "catalogVersion": 1,
        "referenceSpace": {
            "id": "btd6Reference1920x1080",
            "width": 1920,
            "height": 1080,
        },
        "pages": [
            {
                "id": "testPage",
                "kind": "page",
                "minimumScore": 0.9,
                "minimumMatchedAnchors": 1,
                "positiveHoldout": {
                    "evidence": "holdout.json",
                    "evidenceId": holdout_document["evidenceId"],
                    "imageSha256": holdout_hash,
                },
                "anchors": [
                    {
                        "id": "testPage.anchor",
                        "bounds": {"x": 0, "y": 0, "width": 10, "height": 10},
                        "template": "template.png",
                        "templateSha256": template_hash,
                        "minimumScore": 0.9,
                        "sourceEvidence": "source.json",
                        "sourceEvidenceId": source_document["evidenceId"],
                        "sourceImageSha256": source_hash,
                    }
                ],
                "elements": [
                    {
                        "id": "testPage.button",
                        "role": "button",
                        "bounds": {"x": 0, "y": 0, "width": 10, "height": 10},
                        "actionPoint": {"x": 5, "y": 5},
                    }
                ],
            }
        ],
    }
    catalog_path = root / "catalog.json"
    catalog_path.write_text(json.dumps(document), encoding="utf-8")
    return catalog_path


if __name__ == "__main__":
    unittest.main()
