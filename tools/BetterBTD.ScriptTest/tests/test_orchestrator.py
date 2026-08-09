from __future__ import annotations

import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from betterbtd_script_test.api_client import TestApiClientError
from betterbtd_script_test.game_driver_client import (
    GameDriverClientError,
    GameDriverObservation,
)
from betterbtd_script_test.orchestrator import (
    OrchestrationError,
    cancel_and_gate,
    run_act_and_assert,
)
from betterbtd_script_test.scenario import GameState


class OrchestratorTests(unittest.TestCase):
    def test_lost_execute_response_preserves_act_starting_for_cleanup(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            api = _LostExecuteApi([])
            driver = _FakeDriver(
                [self._observation(context, "arrange", 0, "inLevel", "inLevel.roundReady")]
            )

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = run_act_and_assert(
                    context.scenario_path,
                    context.run_id,
                    api,
                    driver,
                    sleep=lambda _: None,
                )

            session = json.loads(context.manifest_path.read_text(encoding="utf-8"))
            self.assertEqual("InfrastructureError", outcome.result)
            self.assertIsNone(outcome.operation_id)
            self.assertEqual("ActStarting", session["phase"])

    def test_cancel_and_gate_can_adopt_same_script_after_lost_execute_response(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            manifest.update(
                {
                    "phase": "ActStarting",
                    "operationId": None,
                    "updatedAtUtc": "2026-08-09T00:00:00.000Z",
                }
            )
            candidate = _status("Running", gate=False)
            candidate["nonOracleDiagnostics"] = {"scriptPath": str(context.script_path)}
            candidate["acceptedAt"] = "2026-08-09T00:00:01.000Z"
            api = _FakeApi(
                [candidate, _status("Running", gate=False), _status("Cancelled", gate=True)]
            )

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = cancel_and_gate(
                    context.scenario_path,
                    context.run_id,
                    api,
                    sleep=lambda _: None,
                )

            self.assertTrue(api.cancelled)
            self.assertTrue(outcome.recover_authorized)

    def test_cancel_and_gate_rejects_operation_outside_execute_handoff(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            manifest.update(
                {
                    "phase": "ActStarting",
                    "operationId": None,
                    "updatedAtUtc": "2026-08-09T00:00:00.000Z",
                }
            )
            candidate = _status("Running", gate=False)
            candidate["nonOracleDiagnostics"] = {
                "scriptPath": str(context.script_path)
            }
            candidate["acceptedAt"] = "2026-08-09T00:01:01.000Z"
            api = _FakeApi([candidate])

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                with self.assertRaisesRegex(
                    OrchestrationError,
                    "not accepted during this session's execute handoff",
                ):
                    cancel_and_gate(context.scenario_path, context.run_id, api)

            self.assertFalse(api.cancelled)

    def test_cancel_and_gate_stops_persisted_operation_before_recover(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            manifest.update({"phase": "Act", "operationId": "test-operation"})
            api = _FakeApi(
                [_status("Running", gate=False), _status("Cancelled", gate=True)]
            )

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = cancel_and_gate(
                    context.scenario_path,
                    context.run_id,
                    api,
                    sleep=lambda _: None,
                )

            self.assertTrue(api.cancelled)
            self.assertTrue(outcome.recover_authorized)
            self.assertEqual("InfrastructureError", outcome.result)

    def test_completed_api_and_independent_victory_pass(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            api = _FakeApi(
                [
                    _status("Running", gate=False),
                    _status("Completed", gate=True),
                    _status("Completed", gate=True),
                ]
            )
            driver = _FakeDriver(
                [
                    self._observation(context, "arrange", 0, "inLevel", "inLevel.roundReady"),
                    self._observation(context, "act", 0, "inLevel", "inLevel.roundActive"),
                    self._observation(context, "assert", 0, "victorySummary"),
                ]
            )

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = run_act_and_assert(
                    context.scenario_path,
                    context.run_id,
                    api,
                    driver,
                    sleep=lambda _: None,
                )

            self.assertEqual("Passed", outcome.result)
            self.assertTrue(outcome.recover_authorized)
            report = json.loads(outcome.report_path.read_text(encoding="utf-8"))
            self.assertEqual("victorySummary", report["assertions"]["all"][0]["evidence"]["pageId"])
            self.assertIn("nonOracleDiagnostics", report)
            self.assertNotIn("statusTimeline", report)
            self.assertFalse(api.cancelled)

    def test_api_completed_cannot_pass_with_unknown_oracle(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            api = _FakeApi(
                [
                    _status("Completed", gate=True),
                    _status("Completed", gate=True),
                ]
            )
            unknown = self._observation(context, "assert", 0, "victorySummary")
            unknown.interpretation["recognition"].update(
                {"status": "unknown", "oracleEligible": False, "page": None}
            )
            driver = _FakeDriver(
                [
                    self._observation(context, "arrange", 0, "inLevel", "inLevel.roundReady"),
                    unknown,
                ]
            )
            clock = _AdvancingClock(0.6)

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = run_act_and_assert(
                    context.scenario_path,
                    context.run_id,
                    api,
                    driver,
                    monotonic=clock,
                    sleep=lambda _: None,
                )

            self.assertEqual("InfrastructureError", outcome.result)
            report = json.loads(outcome.report_path.read_text(encoding="utf-8"))
            codes = {item["code"] for item in report["infrastructureErrors"]}
            self.assertIn("observationNotOracleEligible", codes)

    def test_page_scoped_negative_is_not_unevaluable_on_another_page(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            context.document["assert"]["neverObserved"] = [
                {
                    "id": "no-defeat-retry",
                    "kind": "Element",
                    "operator": "Visible",
                    "elementId": "defeatSummary.retry",
                }
            ]
            api = _FakeApi(
                [
                    _status("Running", gate=False),
                    _status("Completed", gate=True),
                    _status("Completed", gate=True),
                ]
            )
            driver = _FakeDriver(
                [
                    self._observation(
                        context, "arrange", 0, "inLevel", "inLevel.roundReady"
                    ),
                    self._observation(
                        context, "act", 0, "inLevel", "inLevel.roundActive"
                    ),
                    self._observation(context, "assert", 0, "victorySummary"),
                ]
            )

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = run_act_and_assert(
                    context.scenario_path,
                    context.run_id,
                    api,
                    driver,
                    sleep=lambda _: None,
                )

            self.assertEqual("Passed", outcome.result)
            report = json.loads(outcome.report_path.read_text(encoding="utf-8"))
            self.assertEqual([], report["infrastructureErrors"])

    def test_driver_failure_after_execute_cancels_before_recover_authorization(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            api = _FakeApi([_status("Running", gate=False), _status("Cancelled", gate=True)])
            driver = _FakeDriver(
                [self._observation(context, "arrange", 0, "inLevel", "inLevel.roundReady")],
                fail_after_observations=True,
            )

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = run_act_and_assert(
                    context.scenario_path,
                    context.run_id,
                    api,
                    driver,
                    sleep=lambda _: None,
                )

            self.assertEqual("InfrastructureError", outcome.result)
            self.assertTrue(api.cancelled)
            self.assertTrue(outcome.recover_authorized)

    def test_terminal_without_complete_gate_remains_recover_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            context, manifest = self._context(Path(temporary_directory))
            api = _FakeApi([_status("Failed", gate=False)] * 8)
            driver = _FakeDriver(
                [self._observation(context, "arrange", 0, "inLevel", "inLevel.roundReady")]
            )
            clock = _AdvancingClock(0.6)

            with patch(
                "betterbtd_script_test.orchestrator.load_session",
                return_value=(context, manifest),
            ):
                outcome = run_act_and_assert(
                    context.scenario_path,
                    context.run_id,
                    api,
                    driver,
                    monotonic=clock,
                    sleep=lambda _: None,
                )

            self.assertEqual("InfrastructureError", outcome.result)
            self.assertFalse(outcome.recover_authorized)

    @staticmethod
    def _context(root: Path):
        artifact_directory = root / "artifacts"
        artifact_directory.mkdir()
        scenario_path = root / "scenario.json"
        script_path = root / "script.json"
        scenario_path.write_text("{}", encoding="utf-8")
        script_path.write_text("{}", encoding="utf-8")
        document = {
            "id": "unit-scenario",
            "script": {"timeoutMs": 1000},
            "arrange": {
                "readyWhen": [
                    {"id": "ready-page", "kind": "Page", "operator": "Equals", "pageId": "inLevel"},
                    {
                        "id": "ready-state",
                        "kind": "ViewState",
                        "operator": "Equals",
                        "pageId": "inLevel",
                        "viewStateId": "inLevel.roundReady",
                    },
                ]
            },
            "act": {
                "observationIntervalMs": 100,
                "expectedTerminalStatus": "Completed",
            },
            "assert": {
                "timeoutMs": 1000,
                "observationIntervalMs": 100,
                "all": [
                    {
                        "id": "victory",
                        "kind": "Page",
                        "operator": "OneOf",
                        "pageIds": ["victoryPlayerStats", "victorySummary"],
                        "quantifier": "Eventually",
                        "observationWindow": "Assert",
                    }
                ],
                "neverObserved": [
                    {"id": "no-defeat", "kind": "Page", "operator": "Equals", "pageId": "defeatSummary"}
                ],
            },
            "recover": {"timeoutMs": 1000, "targetWhen": []},
        }
        context = SimpleNamespace(
            scenario_path=scenario_path,
            script_path=script_path,
            scenario_id="unit-scenario",
            run_id="unit-run",
            artifact_directory=artifact_directory,
            manifest_path=artifact_directory / "session.json",
            document=document,
            validation=SimpleNamespace(
                expected_game_state=GameState("MonkeyMeadow", "Easy", "Standard", "Quincy")
            ),
        )
        manifest = {
            "phase": "Arrange",
            "operationId": None,
            "recoverAuthorized": False,
        }
        return context, manifest

    @staticmethod
    def _observation(context, phase: str, index: int, page_id: str, view_state: str | None = None):
        directory = context.artifact_directory / phase / str(index)
        directory.mkdir(parents=True, exist_ok=True)
        page = {"id": page_id}
        if view_state is not None:
            page["viewState"] = {
                "status": "matched",
                "oracleEligible": True,
                "state": {"id": view_state},
            }
        interpretation = {
            "recognition": {
                "status": "matched",
                "oracleEligible": True,
                "page": page,
                "elements": [],
            }
        }
        return GameDriverObservation(
            phase=phase,
            index=index,
            directory=directory,
            evidence_path=directory / "evidence.json",
            recognition_path=directory / "recognition.json",
            capture={"capturedAtUtc": "2026-08-09T00:00:00Z", "window": {"handle": "0x1234"}},
            interpretation=interpretation,
        )


class _FakeDriver:
    def __init__(self, observations, *, fail_after_observations: bool = False):
        self._observations = list(observations)
        self._fail_after_observations = fail_after_observations

    def validate_catalog(self):
        return {"valid": True}

    def observe(self, *args, **kwargs):
        if self._observations:
            return self._observations.pop(0)
        if self._fail_after_observations:
            raise GameDriverClientError("capture failed")
        raise AssertionError("unexpected observation")


class _FakeApi:
    token = "x" * 32

    def __init__(self, statuses):
        self._statuses = list(statuses)
        self.cancelled = False

    def redact_text(self, value):
        return value.replace(self.token, "<redacted>")

    def health(self):
        return {
            "nonOracleDiagnostics": {
                "scriptExecutor": {
                    "isRunning": False,
                    "isAutoTaskRunning": False,
                    "isRobotTaskRunning": False,
                }
            }
        }

    def start_capture(self, window_handle):
        return {"started": True, "windowHandle": window_handle}

    def validate_script(self, path):
        return {
            "sha256": "a" * 64,
            "nonOracleDiagnostics": {
                "map": "MonkeyMeadow",
                "difficulty": "Easy",
                "mode": "Standard",
                "hero": "Quincy",
            },
        }

    def execute_script(self, payload):
        return {"operationId": "test-operation", "status": "Starting"}

    def get_status(self, operation_id=None):
        if len(self._statuses) > 1:
            return self._statuses.pop(0)
        return self._statuses[0]

    def get_logs(self, operation_id, after_sequence=0, limit=200):
        return {
            "nextSequence": after_sequence,
            "hasMore": False,
            "isTruncated": False,
            "firstAvailableSequence": 0,
            "nonOracleDiagnostics": {"entries": []},
        }

    def cancel(self, operation_id):
        self.cancelled = True
        return {"operationId": operation_id, "accepted": True}


class _LostExecuteApi(_FakeApi):
    def execute_script(self, payload):
        raise TestApiClientError("transportError", "execute response was lost")


class _AdvancingClock:
    def __init__(self, step: float):
        self._value = 0.0
        self._step = step

    def __call__(self):
        self._value += self._step
        return self._value


def _status(status: str, *, gate: bool):
    return {
        "operationId": "test-operation",
        "status": status,
        "inputOwner": "None" if gate else "BetterBTD",
        "inputControlReleased": gate,
        "canGameDriverRecover": gate,
        "nonOracleDiagnostics": {},
    }


if __name__ == "__main__":
    unittest.main()
