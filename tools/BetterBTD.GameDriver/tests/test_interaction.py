from __future__ import annotations

from dataclasses import replace
from datetime import datetime
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock, patch

from PIL import Image

from betterbtd_game_driver.evidence import read_evidence
from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.interaction import (
    ClickRequest,
    DragPointRequest,
    InteractionDriver,
    PointClickRequest,
    ScrollPointRequest,
    VisualTransitionTracker,
    _validate_interaction_outputs,
    _resolve_click_target,
    resolve_interaction_output_directory,
)
from betterbtd_game_driver.models import Rect, WindowSelector, WindowSnapshot
from betterbtd_game_driver.vision import recognize_image
from betterbtd_game_driver.visual_catalog import load_visual_catalog
from visual_test_support import write_test_evidence


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

    def test_unchanged_stable_frames_are_counted_for_explicit_scroll_boundary(self) -> None:
        frame = Image.new("RGB", (1920, 1080), "black")
        tracker = VisualTransitionTracker(
            frame,
            change_threshold=0.005,
            stability_threshold=0.02,
            stable_sample_count=2,
        )

        first, _ = tracker.observe(frame, elapsed_ms=200, fingerprint="same-1")
        second, _ = tracker.observe(frame, elapsed_ms=400, fingerprint="same-2")

        self.assertEqual(1, first["consecutiveUnchangedStableSamples"])
        self.assertEqual(2, second["consecutiveUnchangedStableSamples"])

    def test_transient_change_that_returns_to_before_does_not_complete_as_changed(self) -> None:
        before = Image.new("RGB", (1920, 1080), "black")
        tracker = VisualTransitionTracker(
            before,
            change_threshold=0.005,
            stability_threshold=0.02,
            stable_sample_count=2,
        )

        _, changed_complete = tracker.observe(
            Image.new("RGB", (1920, 1080), "white"),
            elapsed_ms=200,
            fingerprint="changed",
        )
        returned, returned_complete = tracker.observe(
            before,
            elapsed_ms=400,
            fingerprint="returned",
        )
        stable, stable_complete = tracker.observe(
            before,
            elapsed_ms=600,
            fingerprint="stable",
        )

        self.assertFalse(changed_complete)
        self.assertFalse(returned_complete)
        self.assertFalse(stable_complete)
        self.assertFalse(returned["currentlyChanged"])
        self.assertEqual(1, stable["consecutiveUnchangedStableSamples"])


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
                "heroSelect.AdmiralBrickell": (405, 990),
                "heroSelect.detailsScrollUp": (1600, 1005),
            },
            "hero-select-bottom.zh-CN.holdout.json": {
                "heroSelect.Silas": (100, 520),
                "heroSelect.Psi": (100, 900),
                "heroSelect.Geraldo": (255, 900),
                "heroSelect.Corvus": (405, 900),
            },
            "hero-select-choice.zh-CN.json": {
                "heroSelect.choose": (1120, 615),
            },
            "in-level.zh-CN.holdout.json": {
                "inLevel.settings": (1600, 50),
                "inLevel.startOrFastForward": (1840, 1020),
            },
            "overwrite-save-confirmation.zh-CN.holdout.json": {
                "overwriteSaveConfirmation.cancel": (780, 730),
                "overwriteSaveConfirmation.confirm": (1135, 730),
            },
            "chimps-mode-info.zh-CN.holdout.json": {
                "chimpsModeInfo.ok": (960, 755),
            },
            "defeat-summary.zh-CN.holdout.json": {
                "defeatSummary.home": (740, 810),
                "defeatSummary.restart": (960, 810),
                "defeatSummary.browseMaps": (1180, 810),
            },
            "restart-game-confirmation.zh-CN.holdout.json": {
                "restartGameConfirmation.cancel": (780, 730),
                "restartGameConfirmation.confirm": (1135, 730),
            },
            "post-game-map-review.zh-CN.holdout.json": {
                "postGameMapReview.continue": (1765, 980),
            },
            "victory-player-stats.zh-CN.holdout.json": {
                "victoryPlayerStats.next": (960, 905),
            },
            "victory-summary.zh-CN.holdout.json": {
                "victorySummary.home": (720, 850),
                "victorySummary.browseMaps": (960, 850),
                "victorySummary.freeplay": (1200, 850),
            },
            "freeplay-prompt.zh-CN.holdout.json": {
                "freeplayPrompt.ok": (960, 755),
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

    def test_unlocked_ascent_is_actionable_and_locked_variant_is_not(self) -> None:
        unlocked_evidence = read_evidence(
            SAMPLE_ROOT / "map-select-ascent-unlocked.zh-CN.holdout.json"
        )
        unlocked_recognition, unlocked_page = recognize_image(
            unlocked_evidence,
            self.catalog,
        )

        target = _resolve_click_target(
            unlocked_recognition,
            unlocked_page,
            "mapSelect.ascent",
            unlocked_evidence,
        )

        self.assertEqual((535, 270), target.action_point)

        locked_evidence = read_evidence(
            SAMPLE_ROOT / "map-select-page-11.zh-CN.holdout.json"
        )
        locked_recognition, locked_page = recognize_image(
            locked_evidence,
            self.catalog,
        )
        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                locked_recognition,
                locked_page,
                "mapSelect.ascent",
                locked_evidence,
            )

        self.assertEqual("elementNotVisible", context.exception.code)

        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                locked_recognition,
                locked_page,
                "mapSelect.ascentLocked",
                locked_evidence,
            )

        self.assertEqual("elementNotActionable", context.exception.code)

    def test_hero_without_a_placement_in_current_view_state_is_rejected(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "hero-select.zh-CN.holdout.json")
        recognition, page_match = recognize_image(evidence, self.catalog)

        with self.assertRaises(GameDriverError) as context:
            _resolve_click_target(
                recognition,
                page_match,
                "heroSelect.Corvus",
                evidence,
            )

        self.assertEqual("elementNotVisible", context.exception.code)

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


class ClickViewStateExpectationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog()
        cls.snapshot = WindowSnapshot(
            handle=123,
            process_id=456,
            process_name="BloonsTD6",
            title="BloonsTD6",
            visible=True,
            minimized=False,
            foreground=True,
            dpi=192,
            window_rect=Rect(607, 138, 1946, 1151),
            client_rect=Rect(620, 196, 1920, 1080),
        )

    def test_click_and_click_point_record_matching_final_view_state(self) -> None:
        cases = (
            ("click", "clickElement"),
            ("click-point", "clickClientPoint"),
        )

        for request_kind, expected_operation in cases:
            with (
                self.subTest(request_kind=request_kind),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                output_directory = Path(temporary_directory) / request_kind
                game_driver = self._game_driver_with_frames(
                    SAMPLE_ROOT / "hero-select.zh-CN.holdout.png",
                    SAMPLE_ROOT / "hero-select-bottom.zh-CN.holdout.png",
                )
                common = self._request_arguments(output_directory)
                request = (
                    ClickRequest(element_id="heroSelect.Silas", **common)
                    if request_kind == "click"
                    else PointClickRequest(reference_x=100, reference_y=800, **common)
                )
                interaction = InteractionDriver(game_driver)

                with patch.object(
                    interaction,
                    "_wait_for_transition",
                    return_value=([{"elapsedMs": 200}], "changedStable"),
                ):
                    result = (
                        interaction.click(request, self.catalog)
                        if isinstance(request, ClickRequest)
                        else interaction.click_point(request, self.catalog)
                    )

                trace = json.loads(
                    (output_directory / "operation.json").read_text(encoding="utf-8")
                )

            self.assertEqual(expected_operation, result["operation"])
            self.assertEqual(
                "heroSelect.bottom",
                trace["expectation"]["viewStateId"],
            )
            self.assertTrue(trace["expectation"]["viewStateMatched"])

    def test_click_point_rejects_mismatched_final_view_state_after_writing_trace(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output_directory = Path(temporary_directory) / "mismatch"
            game_driver = self._game_driver_with_frames(
                SAMPLE_ROOT / "hero-select.zh-CN.holdout.png",
                SAMPLE_ROOT / "hero-select.zh-CN.holdout.png",
            )
            request = PointClickRequest(
                reference_x=100,
                reference_y=800,
                **self._request_arguments(output_directory),
            )
            interaction = InteractionDriver(game_driver)

            with (
                patch.object(
                    interaction,
                    "_wait_for_transition",
                    return_value=([{"elapsedMs": 200}], "changedStable"),
                ),
                self.assertRaises(GameDriverError) as context,
            ):
                interaction.click_point(request, self.catalog)

            trace = json.loads(
                (output_directory / "operation.json").read_text(encoding="utf-8")
            )

        self.assertEqual("expectedViewStateNotObserved", context.exception.code)
        self.assertEqual("heroSelect.bottom", trace["expectation"]["viewStateId"])
        self.assertFalse(trace["expectation"]["viewStateMatched"])

    def test_final_expectations_reject_non_oracle_evidence(self) -> None:
        cases = (
            ("page", "heroSelect", "expectedPageNotOracleEligible"),
            ("view", None, "expectedViewStateNotOracleEligible"),
        )

        for case_name, expected_page_id, expected_code in cases:
            with (
                self.subTest(case=case_name),
                tempfile.TemporaryDirectory() as temporary_directory,
            ):
                output_directory = Path(temporary_directory) / case_name
                game_driver = self._game_driver_with_frames(
                    SAMPLE_ROOT / "hero-select.zh-CN.holdout.png",
                    SAMPLE_ROOT / "hero-select-bottom.zh-CN.holdout.png",
                    after_warnings=[
                        {"code": "testWarning", "message": "Synthetic warning."}
                    ],
                )
                arguments = self._request_arguments(output_directory)
                arguments["expected_page_id"] = expected_page_id
                request = PointClickRequest(
                    reference_x=100,
                    reference_y=800,
                    **arguments,
                )
                interaction = InteractionDriver(game_driver)

                with (
                    patch.object(
                        interaction,
                        "_wait_for_transition",
                        return_value=([{"elapsedMs": 200}], "changedStable"),
                    ),
                    self.assertRaises(GameDriverError) as context,
                ):
                    interaction.click_point(request, self.catalog)

                trace = json.loads(
                    (output_directory / "operation.json").read_text(encoding="utf-8")
                )

            self.assertEqual(expected_code, context.exception.code)
            self.assertFalse(trace["expectation"]["pageOracleEligible"])
            self.assertFalse(trace["expectation"]["viewStateOracleEligible"])
            if expected_page_id is not None:
                self.assertFalse(trace["expectation"]["matched"])
            self.assertFalse(trace["expectation"]["viewStateMatched"])

    def _request_arguments(self, output_directory: Path) -> dict[str, object]:
        return {
            "selector": WindowSelector(handle=123),
            "phase": "arrange",
            "output_directory": output_directory,
            "launch_path": None,
            "overwrite": False,
            "expected_page_id": "heroSelect",
            "expected_view_state_id": "heroSelect.bottom",
            "settle_ms": 0,
            "activation_timeout_ms": 1_000,
            "window_timeout_ms": 0,
            "launch_timeout_ms": 0,
            "transition_timeout_ms": 5_000,
            "poll_interval_ms": 100,
            "stable_sample_count": 2,
            "change_threshold": 0.005,
            "stability_threshold": 0.002,
        }

    def _game_driver_with_frames(
        self,
        before: Path,
        after: Path,
        *,
        after_warnings: list[dict[str, str]] | None = None,
    ) -> MagicMock:
        window_api = MagicMock()
        window_api.activate.return_value = True
        window_api.snapshot.return_value = self.snapshot
        window_api.click_client_point.return_value = (720, 996)
        game_driver = MagicMock()
        game_driver.window_api = window_api
        frames = iter(((before, None), (after, after_warnings)))

        def capture(request):
            request.output_path.parent.mkdir(parents=True, exist_ok=True)
            frame, warnings = next(frames)
            with Image.open(frame) as source:
                write_test_evidence(
                    request.output_path.parent,
                    request.output_path.stem,
                    source.convert("RGB"),
                    warnings=warnings,
                )
            return {"window": self.snapshot.to_dict()}

        game_driver.capture.side_effect = capture
        return game_driver


class DragInteractionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog()

    def test_drag_writes_two_endpoint_trace_and_checks_final_view_state(self) -> None:
        snapshot = WindowSnapshot(
            handle=123,
            process_id=456,
            process_name="BloonsTD6",
            title="BloonsTD6",
            visible=True,
            minimized=False,
            foreground=True,
            dpi=192,
            window_rect=Rect(607, 138, 1946, 1151),
            client_rect=Rect(620, 196, 1920, 1080),
        )
        window_api = MagicMock()
        window_api.activate.return_value = True
        window_api.snapshot.return_value = snapshot
        window_api.drag_client_points.return_value = (636, 696, 636, 1096)
        game_driver = MagicMock()
        game_driver.window_api = window_api

        with tempfile.TemporaryDirectory() as temporary_directory:
            output_directory = Path(temporary_directory) / "drag-trace"
            frames = iter(
                (
                    SAMPLE_ROOT / "hero-select.zh-CN.holdout.png",
                    SAMPLE_ROOT / "hero-select-bottom.zh-CN.holdout.png",
                )
            )

            def capture(request):
                request.output_path.parent.mkdir(parents=True, exist_ok=True)
                with Image.open(next(frames)) as source:
                    write_test_evidence(
                        request.output_path.parent,
                        request.output_path.stem,
                        source.convert("RGB"),
                    )
                return {"window": snapshot.to_dict()}

            game_driver.capture.side_effect = capture
            request = DragPointRequest(
                selector=WindowSelector(handle=123),
                phase="arrange",
                output_directory=output_directory,
                launch_path=None,
                overwrite=False,
                expected_page_id="heroSelect",
                settle_ms=0,
                activation_timeout_ms=1_000,
                window_timeout_ms=0,
                launch_timeout_ms=0,
                transition_timeout_ms=5_000,
                poll_interval_ms=100,
                stable_sample_count=2,
                change_threshold=0.005,
                stability_threshold=0.002,
                start_reference_x=16,
                start_reference_y=500,
                end_reference_x=16,
                end_reference_y=900,
                duration_ms=600,
                steps=12,
                allow_no_change=False,
                expected_view_state_id="heroSelect.bottom",
            )
            interaction = InteractionDriver(game_driver)
            observations = [{"elapsedMs": 200, "currentlyChanged": True}]

            with patch.object(
                interaction,
                "_wait_for_transition",
                return_value=(observations, "changedStable"),
            ):
                result = interaction.drag_point(request, self.catalog)

            trace = json.loads(
                (output_directory / "operation.json").read_text(encoding="utf-8")
            )

        window_api.drag_client_points.assert_called_once_with(
            123,
            16,
            500,
            16,
            900,
            600,
            12,
        )
        window_api.click_client_point.assert_not_called()
        window_api.scroll_client_point.assert_not_called()
        self.assertEqual("dragClientPoints", result["operation"])
        self.assertEqual("changedStable", trace["transition"]["status"])
        self.assertEqual(observations, trace["transition"]["observations"])
        self.assertEqual(
            {"x": 16, "y": 500},
            trace["input"]["referenceStartPoint"],
        )
        self.assertEqual(
            {"x": 16, "y": 900},
            trace["input"]["referenceEndPoint"],
        )
        self.assertEqual({"x": 636, "y": 696}, trace["input"]["screenStartPoint"])
        self.assertEqual({"x": 636, "y": 1096}, trace["input"]["screenEndPoint"])
        self.assertEqual(600, trace["input"]["durationMs"])
        self.assertEqual(12, trace["input"]["stepCount"])
        self.assertEqual("heroSelect", trace["expectation"]["pageId"])
        self.assertTrue(trace["expectation"]["matched"])
        self.assertEqual(
            "heroSelect.bottom",
            trace["expectation"]["viewStateId"],
        )
        self.assertTrue(trace["expectation"]["viewStateMatched"])

    def test_drag_rejects_assert_phase_before_capture_or_input(self) -> None:
        game_driver = MagicMock()
        request = DragPointRequest(
            selector=WindowSelector(handle=123),
            phase="assert",
            output_directory=None,
            launch_path=None,
            overwrite=False,
            expected_page_id=None,
            settle_ms=0,
            activation_timeout_ms=1_000,
            window_timeout_ms=0,
            launch_timeout_ms=0,
            transition_timeout_ms=5_000,
            poll_interval_ms=100,
            stable_sample_count=2,
            change_threshold=0.005,
            stability_threshold=0.002,
            start_reference_x=16,
            start_reference_y=500,
            end_reference_x=16,
            end_reference_y=900,
            duration_ms=600,
            steps=12,
            allow_no_change=False,
            expected_view_state_id=None,
        )

        with self.assertRaisesRegex(GameDriverError, "phase must be arrange or recover"):
            InteractionDriver(game_driver).drag_point(request, self.catalog)

        game_driver.capture.assert_not_called()

    def test_direct_scroll_and_drag_requests_preserve_cli_safety_bounds(self) -> None:
        game_driver = MagicMock()
        drag = DragPointRequest(
            selector=WindowSelector(handle=123),
            phase="arrange",
            output_directory=None,
            launch_path=None,
            overwrite=False,
            expected_page_id=None,
            settle_ms=0,
            activation_timeout_ms=1_000,
            window_timeout_ms=0,
            launch_timeout_ms=0,
            transition_timeout_ms=5_000,
            poll_interval_ms=100,
            stable_sample_count=2,
            change_threshold=0.005,
            stability_threshold=0.002,
            start_reference_x=16,
            start_reference_y=500,
            end_reference_x=16,
            end_reference_y=900,
            duration_ms=600,
            steps=12,
            allow_no_change=False,
            expected_view_state_id=None,
        )
        scroll = ScrollPointRequest(
            selector=WindowSelector(handle=123),
            phase="arrange",
            output_directory=None,
            launch_path=None,
            overwrite=False,
            expected_page_id=None,
            settle_ms=0,
            activation_timeout_ms=1_000,
            window_timeout_ms=0,
            launch_timeout_ms=0,
            transition_timeout_ms=5_000,
            poll_interval_ms=100,
            stable_sample_count=2,
            change_threshold=0.005,
            stability_threshold=0.002,
            reference_x=16,
            reference_y=500,
            direction="up",
            notches=21,
            allow_no_change=False,
            expected_view_state_id=None,
        )
        cases = (
            (replace(drag, duration_ms=49), "Drag duration must be between"),
            (replace(drag, duration_ms=5_001), "Drag duration must be between"),
            (replace(drag, steps=101), "Drag steps must be between"),
            (scroll, "Scroll notches must be between"),
        )

        for request, expected_message in cases:
            with (
                self.subTest(message=expected_message),
                self.assertRaisesRegex(GameDriverError, expected_message),
            ):
                if isinstance(request, DragPointRequest):
                    InteractionDriver(game_driver).drag_point(request, self.catalog)
                else:
                    InteractionDriver(game_driver).scroll_point(request, self.catalog)

        game_driver.capture.assert_not_called()


if __name__ == "__main__":
    unittest.main()
