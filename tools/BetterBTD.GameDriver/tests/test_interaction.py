from __future__ import annotations

from datetime import datetime
from pathlib import Path
import tempfile
import unittest

from PIL import Image

from betterbtd_game_driver.evidence import read_evidence
from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.interaction import (
    VisualTransitionTracker,
    _validate_interaction_outputs,
    _resolve_click_target,
    resolve_interaction_output_directory,
)
from betterbtd_game_driver.vision import recognize_image
from betterbtd_game_driver.visual_catalog import load_visual_catalog


SAMPLE_ROOT = Path(__file__).resolve().parent.parent / "visual-baselines" / "samples"


class VisualTransitionTrackerTests(unittest.TestCase):
    def test_changed_frame_must_then_remain_stable(self) -> None:
        tracker = VisualTransitionTracker(
            Image.new("RGB", (1920, 1080), "black"),
            change_threshold=0.05,
            stability_threshold=0.02,
            stable_sample_count=2,
        )

        first, first_complete = tracker.observe(
            Image.new("RGB", (1920, 1080), "white"),
            elapsed_ms=200,
            fingerprint="first",
        )
        second, second_complete = tracker.observe(
            Image.new("RGB", (1920, 1080), "white"),
            elapsed_ms=400,
            fingerprint="second",
        )
        third, third_complete = tracker.observe(
            Image.new("RGB", (1920, 1080), "white"),
            elapsed_ms=600,
            fingerprint="third",
        )

        self.assertTrue(first["changed"])
        self.assertFalse(first_complete)
        self.assertEqual(1, second["consecutiveStableSamples"])
        self.assertFalse(second_complete)
        self.assertEqual(2, third["consecutiveStableSamples"])
        self.assertTrue(third_complete)

    def test_unchanged_frames_never_complete(self) -> None:
        frame = Image.new("RGB", (1920, 1080), "black")
        tracker = VisualTransitionTracker(
            frame,
            change_threshold=0.05,
            stability_threshold=0.02,
            stable_sample_count=1,
        )

        observation, complete = tracker.observe(
            frame,
            elapsed_ms=200,
            fingerprint="same",
        )

        self.assertFalse(observation["changed"])
        self.assertFalse(complete)


class ClickTargetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog()
        cls.evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")
        cls.recognition, cls.page_match = recognize_image(cls.evidence, cls.catalog)

    def test_independently_visible_button_is_actionable(self) -> None:
        target = _resolve_click_target(
            self.recognition,
            self.page_match,
            "mainMenu.start",
            self.evidence,
        )

        self.assertEqual((960, 950), target.action_point)

    def test_new_independently_visible_buttons_resolve_expected_action_points(self) -> None:
        cases = {
            "welcome.zh-CN.holdout.json": {
                "welcome.start": (960, 970),
            },
            "modified-client-warning.zh-CN.holdout.json": {
                "modifiedClientWarning.continue": (452, 515),
            },
            "difficulty-select.zh-CN.holdout.json": {
                "difficultySelect.back": (77, 57),
                "difficultySelect.easy": (630, 400),
                "difficultySelect.medium": (970, 400),
                "difficultySelect.hard": (1300, 400),
            },
            "easy-mode-select.zh-CN.holdout.json": {
                "easyModeSelect.back": (77, 57),
                "easyModeSelect.standard": (630, 590),
                "easyModeSelect.primaryOnly": (960, 450),
                "easyModeSelect.deflation": (1300, 450),
            },
            "medium-mode-select.zh-CN.holdout.json": {
                "mediumModeSelect.back": (77, 57),
                "mediumModeSelect.standard": (630, 590),
                "mediumModeSelect.militaryOnly": (960, 450),
                "mediumModeSelect.apopalypse": (1280, 450),
                "mediumModeSelect.reverse": (960, 740),
                "mediumModeSelect.sandbox": (1280, 740),
            },
            "hard-mode-select.zh-CN.holdout.json": {
                "hardModeSelect.back": (77, 57),
                "hardModeSelect.sandbox": (300, 590),
                "hardModeSelect.standard": (630, 590),
                "hardModeSelect.magicOnly": (960, 450),
                "hardModeSelect.doubleHpMoabs": (1280, 450),
                "hardModeSelect.halfCash": (1600, 450),
                "hardModeSelect.alternateBloonsRounds": (960, 740),
                "hardModeSelect.impoppable": (1280, 740),
                "hardModeSelect.chimps": (1600, 740),
            },
            "hero-select.zh-CN.holdout.json": {
                "heroSelect.back": (77, 57),
                "heroSelect.Quincy": (100, 220),
                "heroSelect.Gwendolin": (255, 220),
                "heroSelect.Geraldo": (405, 990),
                "heroSelect.detailsScrollUp": (1600, 1005),
            },
            "hero-select-choice.zh-CN.json": {
                "heroSelect.choose": (1120, 615),
            },
            "in-level.zh-CN.holdout.json": {
                "inLevel.settings": (1600, 50),
                "inLevel.startOrFastForward": (1840, 1020),
            },
            "stage-settings.zh-CN.holdout.json": {
                "stageSettings.home": (850, 840),
                "stageSettings.continue": (1295, 840),
            },
            "settings.zh-CN.holdout.json": {
                "settings.back": (77, 57),
                "settings.screenSize": (1875, 87),
                "settings.jukebox": (480, 480),
                "settings.hotkeys": (1155, 705),
                "settings.accessibility": (1355, 705),
                "settings.extras": (1550, 705),
                "settings.patchNotes": (100, 980),
            },
            "hotkeys.zh-CN.holdout.json": {
                "hotkeys.back": (77, 57),
            },
            "accessibility.zh-CN.holdout.json": {
                "accessibility.back": (77, 57),
                "accessibility.ok": (960, 910),
            },
        }
        for evidence_name, targets in cases.items():
            evidence = read_evidence(SAMPLE_ROOT / evidence_name)
            recognition, page_match = recognize_image(evidence, self.catalog)
            for element_id, expected_action_point in targets.items():
                with self.subTest(element_id=element_id):
                    target = _resolve_click_target(
                        recognition,
                        page_match,
                        element_id,
                        evidence,
                    )
                    self.assertEqual(element_id, target.id)
                    self.assertEqual(expected_action_point, target.action_point)

    def test_element_without_visibility_detector_is_rejected(self) -> None:
        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                self.recognition,
                self.page_match,
                "mainMenu.mods",
                self.evidence,
            )

        self.assertEqual("elementVisibilityNotEvaluated", context.exception.code)

    def test_visible_modified_client_unregister_is_not_actionable(self) -> None:
        evidence = read_evidence(
            SAMPLE_ROOT / "modified-client-warning.zh-CN.holdout.json"
        )
        recognition, page_match = recognize_image(evidence, self.catalog)

        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                recognition,
                page_match,
                "modifiedClientWarning.unregister",
                evidence,
            )

        self.assertEqual("elementNotActionable", context.exception.code)

    def test_visible_modified_client_close_game_is_not_actionable(self) -> None:
        evidence = read_evidence(
            SAMPLE_ROOT / "modified-client-warning.zh-CN.holdout.json"
        )
        recognition, page_match = recognize_image(evidence, self.catalog)

        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                recognition,
                page_match,
                "modifiedClientWarning.closeGame",
                evidence,
            )

        self.assertEqual("elementNotActionable", context.exception.code)

    def test_visible_settings_unregister_is_not_actionable(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "settings.zh-CN.holdout.json")
        recognition, page_match = recognize_image(evidence, self.catalog)

        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                recognition,
                page_match,
                "settings.unregister",
                evidence,
            )

        self.assertEqual("elementNotActionable", context.exception.code)


class InteractionOutputTests(unittest.TestCase):
    def test_explicit_output_directory_is_resolved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            requested = Path(temporary_directory) / "trace"

            actual = resolve_interaction_output_directory(
                requested,
                "mainMenu.start",
                datetime(2026, 8, 7),
            )

            self.assertEqual(requested.resolve(), actual)

    def test_overwrite_invalidates_old_result_before_new_input(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output_directory = Path(temporary_directory)
            old_after = output_directory / "after.png"
            old_completion = output_directory / "after.complete.json"
            old_trace = output_directory / "operation.json"
            old_after.write_bytes(b"old evidence")
            old_completion.write_text("old completion", encoding="utf-8")
            old_trace.write_text("old trace", encoding="utf-8")

            _validate_interaction_outputs(output_directory, overwrite=True)

            self.assertTrue(old_after.exists())
            self.assertFalse(old_completion.exists())
            self.assertFalse(old_trace.exists())


if __name__ == "__main__":
    unittest.main()
