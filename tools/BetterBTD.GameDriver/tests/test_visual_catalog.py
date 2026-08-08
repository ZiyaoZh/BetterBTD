import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.visual_catalog import (
    VisualElement,
    load_visual_catalog,
    visual_catalog_summary,
)


class VisualCatalogTests(unittest.TestCase):
    def test_bundled_catalog_and_template_hashes_are_valid(self) -> None:
        catalog = load_visual_catalog()

        self.assertEqual("btd6-ui-independent", catalog.id)
        self.assertEqual(2, catalog.schema_version)
        self.assertEqual(15, catalog.version)
        self.assertEqual((1920, 1080), (catalog.reference_width, catalog.reference_height))
        self.assertEqual(
            [
                "welcome",
                "modifiedClientWarning",
                "mainMenu",
                "mapSelect",
                "difficultySelect",
                "easyModeSelect",
                "mediumModeSelect",
                "hardModeSelect",
                "heroSelect",
                "inLevel",
                "overwriteSaveConfirmation",
                "chimpsModeInfo",
                "defeatSummary",
                "restartGameConfirmation",
                "postGameMapReview",
                "victoryPlayerStats",
                "victorySummary",
                "freeplayPrompt",
                "stageSettings",
                "settings",
                "hotkeys",
                "accessibility",
                "extras",
            ],
            [page.id for page in catalog.pages],
        )
        self.assertEqual(
            [
                4,
                5,
                5,
                126,
                4,
                4,
                6,
                9,
                37,
                4,
                6,
                4,
                5,
                5,
                3,
                4,
                5,
                4,
                4,
                18,
                7,
                11,
                45,
            ],
            [len(page.anchors) for page in catalog.pages],
        )
        self.assertEqual(
            {
                "overwriteSaveConfirmation": "modal",
                "chimpsModeInfo": "modal",
                "defeatSummary": "modal",
                "restartGameConfirmation": "modal",
                "postGameMapReview": "page",
                "victoryPlayerStats": "modal",
                "victorySummary": "modal",
                "freeplayPrompt": "modal",
            },
            {
                page.id: page.kind
                for page in catalog.pages
                if page.id
                in {
                    "overwriteSaveConfirmation",
                    "chimpsModeInfo",
                    "defeatSummary",
                    "restartGameConfirmation",
                    "postGameMapReview",
                    "victoryPlayerStats",
                    "victorySummary",
                    "freeplayPrompt",
                }
            },
        )
        self.assertEqual(
            [4, 3, 5, 3, 3, 3, 5, 3],
            [
                sum(anchor.page_anchor for anchor in page.anchors)
                for page in catalog.pages[10:18]
            ],
        )
        self.assertEqual(
            3,
            sum(anchor.page_anchor for anchor in catalog.pages[8].anchors),
        )
        self.assertEqual(
            3,
            sum(anchor.page_anchor for anchor in catalog.pages[9].anchors),
        )
        map_select = catalog.pages[3]
        self.assertEqual(4, sum(anchor.page_anchor for anchor in map_select.anchors))
        self.assertEqual(17, len(map_select.view_states))
        self.assertEqual(102, len(map_select.elements))
        self.assertEqual(
            207,
            sum(len(element.placements) for element in map_select.elements),
        )
        ascent = next(
            element
            for element in map_select.elements
            if element.id == "mapSelect.ascent"
        )
        self.assertEqual("button", ascent.role)
        self.assertEqual((535, 270), ascent.placements[0].action_point)
        ascent_locked = next(
            element
            for element in map_select.elements
            if element.id == "mapSelect.ascentLocked"
        )
        self.assertEqual("status", ascent_locked.role)
        self.assertIsNone(ascent_locked.placements[0].action_point)
        self.assertEqual(
            [5, 6, 5],
            [
                sum(anchor.page_anchor for anchor in page.anchors)
                for page in catalog.pages[19:22]
            ],
        )
        self.assertTrue(all(page.positive_holdout is not None for page in catalog.pages))
        hero_select = catalog.pages[8]
        self.assertEqual(
            ["heroSelect.top", "heroSelect.bottom"],
            [view_state.id for view_state in hero_select.view_states],
        )
        self.assertEqual(
            30,
            sum(len(element.placements) for element in hero_select.elements),
        )
        self.assertEqual(
            [
                "Quincy",
                "Gwendolin",
                "StrikerJones",
                "ObynGreenfoot",
                "DanDeMonk",
                "Benjamin",
                "PatFusty",
                "CaptainChurchill",
                "Ezili",
                "Silas",
                "Etienne",
                "Sauda",
                "Rosalia",
                "Adora",
                "AdmiralBrickell",
                "Psi",
                "Geraldo",
                "Corvus",
            ],
            [
                element.id.removeprefix("heroSelect.")
                for element in hero_select.elements
                if element.placements
            ],
        )
        extras = catalog.pages[-1]
        self.assertEqual(
            ["extras.top", "extras.bottom"],
            [view_state.id for view_state in extras.view_states],
        )
        self.assertEqual(3, sum(anchor.page_anchor for anchor in extras.anchors))
        self.assertEqual(14, sum(len(element.placements) for element in extras.elements))
        self.assertEqual(
            28,
            sum(
                len(placement.states)
                for element in extras.elements
                for placement in element.placements
            ),
        )
        summary = visual_catalog_summary(catalog)
        self.assertEqual(
            {
                "valid": True,
                "pageCount": 23,
                "templateCount": 325,
                "elementCount": 297,
                "viewStateCount": 21,
                "placementCount": 251,
            },
            summary["validation"],
        )
        elements = [element for page in catalog.pages for element in page.elements]

        def has_detector(element: VisualElement) -> bool:
            return bool(element.anchor_ids) or any(
                placement.anchor_ids for placement in element.placements
            )

        def has_action_point(element: VisualElement) -> bool:
            return element.action_point is not None or any(
                placement.action_point is not None for placement in element.placements
            )

        self.assertEqual(237, sum(has_detector(element) for element in elements))
        self.assertEqual(201, sum(has_action_point(element) for element in elements))
        self.assertEqual(
            178,
            sum(
                has_detector(element) and has_action_point(element)
                for element in elements
            ),
        )

    def test_modal_kind_is_supported_and_other_kinds_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory))
            document = _read_catalog(catalog_path)
            document["pages"][0]["kind"] = "modal"
            _write_catalog(catalog_path, document)

            catalog = load_visual_catalog(catalog_path)

            self.assertEqual("modal", catalog.pages[0].kind)

            document["pages"][0]["kind"] = "overlay"
            _write_catalog(catalog_path, document)
            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertIn("kind must be page or modal", context.exception.message)

    def test_schema_v1_legacy_catalog_remains_supported(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog = load_visual_catalog(_write_minimal_catalog(Path(temporary_directory)))

        self.assertEqual(1, catalog.schema_version)
        self.assertEqual((), catalog.pages[0].view_states)
        self.assertEqual((), catalog.pages[0].elements[0].placements)
        self.assertEqual(
            catalog.pages[0].anchors[0].bounds,
            catalog.pages[0].anchors[0].source_bounds,
        )

    def test_schema_v2_view_state_and_placement_are_loaded(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            catalog = load_visual_catalog(catalog_path)

        page = catalog.pages[0]
        self.assertEqual(2, catalog.schema_version)
        self.assertEqual(["testPage.top"], [state.id for state in page.view_states])
        self.assertEqual(
            ["testPage.top"],
            [placement.view_state_id for placement in page.elements[0].placements],
        )

    def test_view_state_must_only_reference_detector_only_anchors(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            document["pages"][0]["viewStates"][0]["anchorIds"] = ["testPage.anchor"]
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("must use detector-only anchors", context.exception.message)

    def test_view_state_holdout_must_differ_from_template_sources(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            detector = document["pages"][0]["anchors"][1]
            document["pages"][0]["viewStates"][0]["positiveHoldout"] = {
                "evidence": detector["sourceEvidence"],
                "evidenceId": detector["sourceEvidenceId"],
                "imageSha256": detector["sourceImageSha256"],
            }
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("must differ", context.exception.message)

    def test_view_state_holdout_provenance_must_match_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            document["pages"][0]["viewStates"][0]["positiveHoldout"]["evidenceId"] = (
                "wrong-evidence"
            )
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("evidenceId does not match", context.exception.message)

    def test_placement_must_reference_a_view_state_on_the_same_page(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            _placement(document)["viewStateId"] = "testPage.unknown"
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("references unknown view state", context.exception.message)

    def test_placement_bounds_must_be_inside_reference_space(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            _placement(document)["bounds"] = {"x": 1919, "y": 0, "width": 2, "height": 10}
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("placement bounds must be inside", context.exception.message)

    def test_placement_action_point_must_be_inside_placement_bounds(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            _placement(document)["actionPoint"] = {"x": 10, "y": 5}
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("actionPoint must be inside bounds", context.exception.message)

    def test_element_cannot_repeat_a_placement_view_state(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            document["pages"][0]["elements"][0]["placements"].append(
                dict(_placement(document))
            )
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn(
            "duplicate element testPage.button placement viewStateId",
            context.exception.message,
        )

    def test_placement_cannot_repeat_an_element_state_id(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            state = {"id": "enabled", "anchorIds": ["testPage.detector"]}
            _placement(document)["states"] = [state, dict(state)]
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn(
            "duplicate element testPage.button placement state id",
            context.exception.message,
        )

    def test_source_bounds_must_be_inside_reference_space(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory), schema_version=2)
            document = _read_catalog(catalog_path)
            document["pages"][0]["anchors"][0]["sourceBounds"] = {
                "x": 1919,
                "y": 0,
                "width": 2,
                "height": 10,
            }
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("sourceBounds bounds must be inside", context.exception.message)

    def test_schema_v1_rejects_source_bounds(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            catalog_path = _write_minimal_catalog(Path(temporary_directory))
            document = _read_catalog(catalog_path)
            document["pages"][0]["anchors"][0]["sourceBounds"] = dict(
                document["pages"][0]["anchors"][0]["bounds"]
            )
            _write_catalog(catalog_path, document)

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

        self.assertIn("sourceBounds require catalog schemaVersion 2", context.exception.message)

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

    def test_detector_only_anchor_does_not_satisfy_page_minimum(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            document["pages"][0]["anchors"][0]["pageAnchor"] = False
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertEqual("visualCatalogInvalid", context.exception.code)
            self.assertIn("page anchor count", context.exception.message)

    def test_page_anchor_must_be_boolean(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            catalog_path = _write_minimal_catalog(root)
            document = json.loads(catalog_path.read_text(encoding="utf-8"))
            document["pages"][0]["anchors"][0]["pageAnchor"] = "false"
            catalog_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(GameDriverError) as context:
                load_visual_catalog(catalog_path)

            self.assertEqual("visualCatalogInvalid", context.exception.code)
            self.assertIn("pageAnchor must be a boolean", context.exception.message)

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


def _write_minimal_catalog(root: Path, *, schema_version: int = 1) -> Path:
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
        "schemaVersion": schema_version,
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
    if schema_version == 2:
        detector_template_path = root / "detector-template.png"
        detector_template_path.write_bytes(b"detector-template")
        detector_source_path = write_test_evidence(
            root,
            "detector-source",
            Image.new("RGB", (1920, 1080), "gray"),
        )
        detector_source = json.loads(detector_source_path.read_text(encoding="utf-8"))
        detector_source_hash = hashlib.sha256(
            (root / "detector-source.png").read_bytes()
        ).hexdigest()
        state_holdout_path = write_test_evidence(
            root,
            "state-holdout",
            Image.new("RGB", (1920, 1080), "blue"),
        )
        state_holdout = json.loads(state_holdout_path.read_text(encoding="utf-8"))
        state_holdout_hash = hashlib.sha256(
            (root / "state-holdout.png").read_bytes()
        ).hexdigest()
        page = document["pages"][0]
        page["anchors"].append(
            {
                "id": "testPage.detector",
                "bounds": {"x": 20, "y": 0, "width": 10, "height": 10},
                "sourceBounds": {"x": 30, "y": 0, "width": 10, "height": 10},
                "template": "detector-template.png",
                "templateSha256": hashlib.sha256(
                    detector_template_path.read_bytes()
                ).hexdigest(),
                "minimumScore": 0.9,
                "pageAnchor": False,
                "sourceEvidence": "detector-source.json",
                "sourceEvidenceId": detector_source["evidenceId"],
                "sourceImageSha256": detector_source_hash,
            }
        )
        page["viewStates"] = [
            {
                "id": "testPage.top",
                "minimumScore": 0.9,
                "minimumMatchedAnchors": 1,
                "anchorIds": ["testPage.detector"],
                "positiveHoldout": {
                    "evidence": "state-holdout.json",
                    "evidenceId": state_holdout["evidenceId"],
                    "imageSha256": state_holdout_hash,
                },
            }
        ]
        page["elements"] = [
            {
                "id": "testPage.button",
                "role": "button",
                "placements": [
                    {
                        "viewStateId": "testPage.top",
                        "bounds": {"x": 0, "y": 0, "width": 10, "height": 10},
                        "actionPoint": {"x": 5, "y": 5},
                        "anchorIds": ["testPage.detector"],
                    }
                ],
            }
        ]
    catalog_path = root / "catalog.json"
    _write_catalog(catalog_path, document)
    return catalog_path


def _read_catalog(path: Path) -> dict[str, object]:
    return json.loads(path.read_text(encoding="utf-8"))


def _write_catalog(path: Path, document: dict[str, object]) -> None:
    path.write_text(json.dumps(document), encoding="utf-8")


def _placement(document: dict[str, object]) -> dict[str, object]:
    return document["pages"][0]["elements"][0]["placements"][0]


if __name__ == "__main__":
    unittest.main()
