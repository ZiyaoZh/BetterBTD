from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
import re
import time
from typing import Any, Callable, Mapping, Sequence

from .api_client import TestApiClient, TestApiClientError
from .artifacts import (
    SessionContext,
    atomic_write_json,
    load_session,
    update_manifest,
)
from .game_driver_client import (
    GameDriverClient,
    GameDriverObservation,
    next_observation_index,
    observation_window_handle,
)
from .predicates import evaluate_predicate
from .scenario import ScenarioValidationError, validate_script_summary


TERMINAL_STATUSES = frozenset({"Completed", "Failed", "Cancelled", "TimedOut"})
REPORT_SCHEMA = "betterbtd/script-test-report"
REPORT_SCHEMA_VERSION = 1


class OrchestrationError(RuntimeError):
    def __init__(self, code: str, message: str) -> None:
        self.code = code
        self.message = message
        super().__init__(message)


@dataclass(frozen=True)
class RunOutcome:
    result: str
    recover_authorized: bool
    operation_id: str | None
    report_path: Path


def cancel_and_gate(
    scenario_path: Path | str,
    run_id: str,
    api_client: TestApiClient,
    *,
    monotonic: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> RunOutcome:
    context, manifest = load_session(scenario_path, run_id)
    operation_id = manifest.get("operationId")
    status_timeline: list[Mapping[str, Any]] = []
    errors: list[dict[str, Any]] = []
    final_status: Mapping[str, Any] | None = None
    if not isinstance(operation_id, str) or not operation_id:
        if manifest.get("phase") != "ActStarting":
            raise OrchestrationError(
                "operationIdMissing", "session has no operation to cancel"
            )
        candidate = api_client.get_status()
        diagnostics = candidate.get("nonOracleDiagnostics")
        script_path = diagnostics.get("scriptPath") if isinstance(diagnostics, Mapping) else None
        if not isinstance(script_path, str) or Path(script_path).resolve() != context.script_path:
            raise OrchestrationError(
                "operationAdoptionRejected",
                "current Test API operation does not belong to this scenario script",
            )
        act_starting_at = _parse_utc_timestamp(manifest.get("updatedAtUtc"))
        accepted_at = _parse_utc_timestamp(candidate.get("acceptedAt"))
        acceptance_delay = (accepted_at - act_starting_at).total_seconds()
        if acceptance_delay < -5 or acceptance_delay > 60:
            raise OrchestrationError(
                "operationAdoptionRejected",
                "current operation was not accepted during this session's execute handoff",
            )
        operation_id_value = candidate.get("operationId")
        if not isinstance(operation_id_value, str) or not operation_id_value:
            raise OrchestrationError(
                "operationAdoptionRejected", "current operation has no stable ID"
            )
        operation_id = operation_id_value
        manifest = update_manifest(context, manifest, operationId=operation_id)

    try:
        current = api_client.get_status(operation_id)
        status_timeline.append(current)
        if current.get("status") not in TERMINAL_STATUSES:
            try:
                api_client.cancel(operation_id)
            except TestApiClientError as exception:
                if exception.code != "invalidOperationState":
                    raise
        final_status = _wait_for_terminal_and_gate(
            api_client,
            operation_id,
            context.document["recover"]["timeoutMs"] / 1000,
            status_timeline,
            monotonic,
            sleep,
            require_gate=True,
        )
    except TestApiClientError as exception:
        errors.append({"code": exception.code, "message": str(exception)})

    recover_authorized = _recover_gate_satisfied(final_status)
    if not recover_authorized:
        errors.append(
            {
                "code": "recoverNotAuthorized",
                "message": "cancelled operation did not reach the complete Recover gate",
            }
        )
    report_path = context.artifact_directory / "abort.json"
    atomic_write_json(
        report_path,
        {
            "schema": "betterbtd/script-test-abort",
            "schemaVersion": 1,
            "scenarioId": context.scenario_id,
            "runId": context.run_id,
            "result": "InfrastructureError",
            "recoverAuthorized": recover_authorized,
            "operationId": operation_id,
            "infrastructureErrors": errors,
            "nonOracleDiagnostics": {
                "statusTimeline": status_timeline,
                "finalStatus": final_status,
            },
        },
        text_redactor=api_client.redact_text,
    )
    update_manifest(
        context,
        manifest,
        phase="Recover" if recover_authorized else "RecoverBlocked",
        operationId=operation_id,
        recoverAuthorized=recover_authorized,
        result="InfrastructureError",
        abortPath=str(report_path),
    )
    return RunOutcome("InfrastructureError", recover_authorized, operation_id, report_path)


@dataclass
class _AssertionState:
    predicate: Mapping[str, Any]
    evaluable_count: int = 0
    matched: bool = False
    evidence: Mapping[str, Any] | None = None
    last_reason: str | None = None


def run_act_and_assert(
    scenario_path: Path | str,
    run_id: str,
    api_client: TestApiClient,
    game_driver: GameDriverClient,
    *,
    monotonic: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> RunOutcome:
    context, manifest = load_session(scenario_path, run_id)
    if manifest.get("phase") != "Arrange" or manifest.get("operationId") is not None:
        raise OrchestrationError(
            "invalidSessionPhase",
            "run-act-assert requires an unused session in Arrange phase",
        )

    document = context.document
    positive_states = {
        predicate["id"]: _AssertionState(predicate)
        for predicate in document["assert"]["all"]
    }
    negative_states = {
        predicate["id"]: _AssertionState(predicate)
        for predicate in document["assert"]["neverObserved"]
    }
    status_timeline: list[Mapping[str, Any]] = []
    observations: list[Mapping[str, Any]] = []
    logs: list[Mapping[str, Any]] = []
    log_pages: list[Mapping[str, Any]] = []
    infrastructure_errors: list[dict[str, Any]] = []
    failures: list[dict[str, Any]] = []
    next_log_sequence = 0
    operation_id: str | None = None
    final_status: Mapping[str, Any] | None = None
    health: Mapping[str, Any] | None = None
    capture_start: Mapping[str, Any] | None = None
    script_validation: Mapping[str, Any] | None = None
    execute_response: Mapping[str, Any] | None = None

    try:
        game_driver.validate_catalog()
        health = api_client.health()
        _require_idle_health(health)

        arrange_observation = game_driver.observe(
            context,
            phase="arrange",
            index=0,
            activate=True,
        )
        observations.append(arrange_observation.reference(context))
        _require_predicates(
            document["arrange"]["readyWhen"],
            arrange_observation,
            "arrangeNotReady",
        )
        window_handle = observation_window_handle(arrange_observation)
        capture_start = api_client.start_capture(int(window_handle, 0))

        script_validation = api_client.validate_script(context.script_path)
        diagnostics = script_validation.get("nonOracleDiagnostics")
        if not isinstance(diagnostics, Mapping):
            raise OrchestrationError(
                "invalidApiResponse",
                "script validation has no nonOracleDiagnostics object",
            )
        try:
            validate_script_summary(context.validation.expected_game_state, diagnostics)
        except ScenarioValidationError as exception:
            raise OrchestrationError("scriptMetadataMismatch", str(exception)) from exception
        digest = script_validation.get("sha256")
        if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
            raise OrchestrationError(
                "invalidApiResponse", "script validation returned an invalid SHA-256"
            )

        manifest = update_manifest(context, manifest, phase="ActStarting")
        execute_response = api_client.execute_script(
            _execute_request(document["script"], context.script_path, digest)
        )
        operation_id_value = execute_response.get("operationId")
        if not isinstance(operation_id_value, str) or not operation_id_value:
            raise OrchestrationError(
                "invalidApiResponse", "execute response has no operationId"
            )
        operation_id = operation_id_value
        manifest = update_manifest(
            context,
            manifest,
            phase="Act",
            operationId=operation_id,
        )

        act_deadline = monotonic() + (document["script"]["timeoutMs"] / 1000) + 10
        act_index = 0
        while True:
            status = api_client.get_status(operation_id)
            status_timeline.append(status)
            page_logs, next_log_sequence, page_summary = _collect_logs(
                api_client, operation_id, next_log_sequence
            )
            logs.extend(page_logs)
            log_pages.extend(page_summary)
            if status.get("status") in TERMINAL_STATUSES:
                final_status = status
                break
            if monotonic() >= act_deadline:
                failures.append({"code": "actTimeout", "message": "Act timed out"})
                api_client.cancel(operation_id)
                final_status = _wait_for_terminal_and_gate(
                    api_client,
                    operation_id,
                    document["recover"]["timeoutMs"] / 1000,
                    status_timeline,
                    monotonic,
                    sleep,
                    require_gate=False,
                )
                break

            observation = game_driver.observe(
                context,
                phase="act",
                index=act_index,
                window_handle=window_handle,
                activate=False,
            )
            act_index += 1
            reference = observation.reference(context)
            observations.append(reference)
            _evaluate_sample(
                observation,
                reference,
                positive_states,
                negative_states,
                "ActAndAssert",
                infrastructure_errors,
                failures,
            )
            _write_journal(
                context,
                operation_id,
                "Act",
                status_timeline,
                observations,
                next_log_sequence,
                log_pages,
                api_client.redact_text,
            )
            sleep(document["act"]["observationIntervalMs"] / 1000)

        if final_status is None:
            raise OrchestrationError("missingTerminalStatus", "operation has no final status")

        if final_status.get("status") == "Completed" and not failures:
            manifest = update_manifest(context, manifest, phase="Assert")
            assert_deadline = monotonic() + document["assert"]["timeoutMs"] / 1000
            assert_index = 0
            while not _all_positive_matched(positive_states):
                if monotonic() >= assert_deadline:
                    break
                observation = game_driver.observe(
                    context,
                    phase="assert",
                    index=assert_index,
                    window_handle=window_handle,
                    activate=False,
                )
                assert_index += 1
                reference = observation.reference(context)
                observations.append(reference)
                _evaluate_sample(
                    observation,
                    reference,
                    positive_states,
                    negative_states,
                    "Assert",
                    infrastructure_errors,
                    failures,
                )
                _write_journal(
                    context,
                    operation_id,
                    "Assert",
                    status_timeline,
                    observations,
                    next_log_sequence,
                    log_pages,
                    api_client.redact_text,
                )
                if failures or _all_positive_matched(positive_states):
                    break
                sleep(document["assert"]["observationIntervalMs"] / 1000)

        if final_status.get("status") != document["act"]["expectedTerminalStatus"]:
            failures.append(
                {
                    "code": "unexpectedOperationStatus",
                    "expected": document["act"]["expectedTerminalStatus"],
                    "actual": final_status.get("status"),
                }
            )

        for predicate_id, state in positive_states.items():
            if state.matched:
                continue
            if state.evaluable_count == 0:
                infrastructure_errors.append(
                    {
                        "code": "assertionUnevaluable",
                        "predicateId": predicate_id,
                        "reason": state.last_reason,
                    }
                )
            else:
                failures.append(
                    {"code": "assertionNotSatisfied", "predicateId": predicate_id}
                )

        remaining_logs, next_log_sequence, page_summary = _collect_logs(
            api_client, operation_id, next_log_sequence
        )
        logs.extend(remaining_logs)
        log_pages.extend(page_summary)
        final_status = _wait_for_terminal_and_gate(
            api_client,
            operation_id,
            document["recover"]["timeoutMs"] / 1000,
            status_timeline,
            monotonic,
            sleep,
            require_gate=True,
        )
    except KeyboardInterrupt:
        infrastructure_errors.append(
            {"code": "interrupted", "message": "scenario run was interrupted"}
        )
        if operation_id is not None:
            final_status = _cancel_safely(
                api_client,
                operation_id,
                document["recover"]["timeoutMs"] / 1000,
                status_timeline,
                monotonic,
                sleep,
                infrastructure_errors,
            )
    except Exception as exception:
        infrastructure_errors.append(
            {
                "code": getattr(exception, "code", "orchestrationError"),
                "message": str(exception),
            }
        )
        if operation_id is not None:
            final_status = _cancel_safely(
                api_client,
                operation_id,
                document["recover"]["timeoutMs"] / 1000,
                status_timeline,
                monotonic,
                sleep,
                infrastructure_errors,
            )

    recover_authorized = _recover_gate_satisfied(final_status)
    if not recover_authorized and operation_id is not None:
        infrastructure_errors.append(
            {
                "code": "recoverNotAuthorized",
                "message": "operation did not reach the complete Recover input gate",
            }
        )
    result = (
        "InfrastructureError"
        if infrastructure_errors
        else ("Failed" if failures else "Passed")
    )
    report_path = context.artifact_directory / "report.json"
    report = {
        "schema": REPORT_SCHEMA,
        "schemaVersion": REPORT_SCHEMA_VERSION,
        "scenarioId": context.scenario_id,
        "runId": context.run_id,
        "result": result,
        "recoverAuthorized": recover_authorized,
        "operationId": operation_id,
        "assertions": {
            "all": [
                _assertion_report(predicate_id, state)
                for predicate_id, state in positive_states.items()
            ],
            "neverObserved": [
                _assertion_report(predicate_id, state)
                for predicate_id, state in negative_states.items()
            ],
        },
        "failures": failures,
        "infrastructureErrors": infrastructure_errors,
        "observations": observations,
        "nonOracleDiagnostics": {
            "health": health,
            "captureStart": capture_start,
            "scriptValidation": script_validation,
            "execute": execute_response,
            "statusTimeline": status_timeline,
            "finalStatus": final_status,
            "logs": {
                "entries": logs,
                "pages": log_pages,
                "nextSequence": next_log_sequence,
            },
        },
    }
    atomic_write_json(report_path, report, text_redactor=api_client.redact_text)
    if recover_authorized:
        next_phase = "Recover"
    elif manifest.get("phase") == "ActStarting" and operation_id is None:
        next_phase = "ActStarting"
    else:
        next_phase = "RecoverBlocked"
    update_manifest(
        context,
        manifest,
        phase=next_phase,
        operationId=operation_id,
        recoverAuthorized=recover_authorized,
        result=result,
        reportPath=str(report_path),
    )
    return RunOutcome(result, recover_authorized, operation_id, report_path)


def verify_recover_target(
    scenario_path: Path | str,
    run_id: str,
    game_driver: GameDriverClient,
) -> Mapping[str, Any]:
    context, manifest = load_session(scenario_path, run_id)
    if manifest.get("phase") != "Recover" or manifest.get("recoverAuthorized") is not True:
        raise OrchestrationError(
            "recoverNotAuthorized", "session has not been authorized for Recover input"
        )
    report_path = context.artifact_directory / "recovery.json"
    observation = game_driver.observe(
        context,
        phase="recover",
        index=next_observation_index(context, "recover"),
        activate=True,
    )
    _require_predicates(
        context.document["recover"]["targetWhen"],
        observation,
        "recoverTargetNotReached",
    )
    result = {
        "schema": "betterbtd/script-test-recovery",
        "schemaVersion": 1,
        "scenarioId": context.scenario_id,
        "runId": context.run_id,
        "targetReached": True,
        "observation": observation.reference(context),
    }
    atomic_write_json(report_path, result)
    update_manifest(context, manifest, phase="Completed", recoveryPath=str(report_path))
    return result


def _require_idle_health(health: Mapping[str, Any]) -> None:
    diagnostics = health.get("nonOracleDiagnostics")
    executor = diagnostics.get("scriptExecutor") if isinstance(diagnostics, Mapping) else None
    if not isinstance(executor, Mapping):
        raise OrchestrationError(
            "invalidApiResponse", "health response has no scriptExecutor diagnostics"
        )
    if any(
        executor.get(field) is True
        for field in ("isRunning", "isAutoTaskRunning", "isRobotTaskRunning")
    ):
        raise OrchestrationError(
            "gameControlBusy", "BetterBTD reports an active game controller"
        )


def _execute_request(
    script: Mapping[str, Any],
    script_path: Path,
    digest: str,
) -> dict[str, Any]:
    return {
        "scriptPath": str(script_path),
        "expectedSha256": digest,
        "startStepIndex": script.get("startStepIndex", 0),
        "intervalStrategy": script.get("intervalStrategy", "InstructionCustom"),
        "commonOperationIntervalMs": script.get("commonOperationIntervalMs", 200),
        "timeoutMs": script["timeoutMs"],
    }


def _require_predicates(
    predicates: Sequence[Mapping[str, Any]],
    observation: GameDriverObservation,
    code: str,
) -> None:
    failures: list[str] = []
    for predicate in predicates:
        evaluation = evaluate_predicate(predicate, observation.interpretation)
        if not evaluation.evaluable or not evaluation.matched:
            failures.append(f"{predicate['id']}: {evaluation.reason or 'notMatched'}")
    if failures:
        raise OrchestrationError(code, "; ".join(failures))


def _evaluate_sample(
    observation: GameDriverObservation,
    reference: Mapping[str, Any],
    positive_states: Mapping[str, _AssertionState],
    negative_states: Mapping[str, _AssertionState],
    window: str,
    infrastructure_errors: list[dict[str, Any]],
    failures: list[dict[str, Any]],
) -> None:
    recognition = observation.recognition
    if recognition.get("status") != "matched" or recognition.get("oracleEligible") is not True:
        infrastructure_errors.append(
            {
                "code": "observationNotOracleEligible",
                "phase": observation.phase,
                "index": observation.index,
            }
        )

    actual_page = recognition.get("page")
    actual_page_id = actual_page.get("id") if isinstance(actual_page, Mapping) else None
    for predicate_id, state in positive_states.items():
        predicate_window = state.predicate.get("observationWindow")
        if window == "ActAndAssert" and predicate_window != "ActAndAssert":
            continue
        evaluation = evaluate_predicate(state.predicate, observation.interpretation)
        state.last_reason = evaluation.reason
        if evaluation.evaluable:
            state.evaluable_count += 1
        elif _predicate_applies_to_page(state.predicate, actual_page_id):
            infrastructure_errors.append(
                {
                    "code": "assertionSampleUnevaluable",
                    "predicateId": predicate_id,
                    "reason": evaluation.reason,
                    "observation": reference,
                }
            )
        if evaluation.matched and not state.matched:
            state.matched = True
            state.evidence = reference

    for predicate_id, state in negative_states.items():
        evaluation = evaluate_predicate(state.predicate, observation.interpretation)
        state.last_reason = evaluation.reason
        if evaluation.evaluable:
            state.evaluable_count += 1
            if not evaluation.matched and not state.matched:
                state.evidence = reference
        elif _predicate_applies_to_page(state.predicate, actual_page_id):
            infrastructure_errors.append(
                {
                    "code": "negativeAssertionUnevaluable",
                    "predicateId": predicate_id,
                    "reason": evaluation.reason,
                    "observation": reference,
                }
            )
        if evaluation.matched and not state.matched:
            state.matched = True
            state.evidence = reference
            failures.append(
                {
                    "code": "negativeAssertionObserved",
                    "predicateId": predicate_id,
                    "observation": reference,
                }
            )


def _predicate_applies_to_page(
    predicate: Mapping[str, Any], actual_page_id: object
) -> bool:
    if not isinstance(actual_page_id, str):
        return True
    kind = predicate.get("kind")
    if kind == "Page":
        return True
    if kind == "ViewState":
        return predicate.get("pageId") == actual_page_id
    element_id = predicate.get("elementId")
    return isinstance(element_id, str) and element_id.startswith(actual_page_id + ".")


def _all_positive_matched(states: Mapping[str, _AssertionState]) -> bool:
    return all(state.matched for state in states.values())


def _collect_logs(
    api_client: TestApiClient,
    operation_id: str,
    after_sequence: int,
) -> tuple[list[Mapping[str, Any]], int, list[Mapping[str, Any]]]:
    entries: list[Mapping[str, Any]] = []
    pages: list[Mapping[str, Any]] = []
    cursor = after_sequence
    while True:
        response = api_client.get_logs(operation_id, cursor, 1000)
        diagnostics = response.get("nonOracleDiagnostics")
        raw_entries = diagnostics.get("entries") if isinstance(diagnostics, Mapping) else None
        if not isinstance(raw_entries, list) or any(
            not isinstance(entry, Mapping) for entry in raw_entries
        ):
            raise OrchestrationError(
                "invalidApiResponse", "logs response has malformed entries"
            )
        entries.extend(raw_entries)
        next_sequence = response.get("nextSequence")
        if not isinstance(next_sequence, int) or isinstance(next_sequence, bool):
            raise OrchestrationError(
                "invalidApiResponse", "logs response has invalid nextSequence"
            )
        pages.append(
            {
                "requestedAfterSequence": cursor,
                "nextSequence": next_sequence,
                "hasMore": response.get("hasMore") is True,
                "isTruncated": response.get("isTruncated") is True,
                "firstAvailableSequence": response.get("firstAvailableSequence"),
                "entryCount": len(raw_entries),
            }
        )
        if response.get("hasMore") is not True:
            return entries, next_sequence, pages
        if next_sequence <= cursor:
            raise OrchestrationError(
                "invalidApiResponse", "logs pagination did not advance"
            )
        cursor = next_sequence


def _wait_for_terminal_and_gate(
    api_client: TestApiClient,
    operation_id: str,
    timeout_seconds: float,
    timeline: list[Mapping[str, Any]],
    monotonic: Callable[[], float],
    sleep: Callable[[float], None],
    *,
    require_gate: bool,
) -> Mapping[str, Any]:
    deadline = monotonic() + timeout_seconds
    while True:
        status = api_client.get_status(operation_id)
        timeline.append(status)
        terminal = status.get("status") in TERMINAL_STATUSES
        if terminal and (not require_gate or _recover_gate_satisfied(status)):
            return status
        if monotonic() >= deadline:
            return status
        sleep(0.2)


def _cancel_safely(
    api_client: TestApiClient,
    operation_id: str,
    timeout_seconds: float,
    timeline: list[Mapping[str, Any]],
    monotonic: Callable[[], float],
    sleep: Callable[[float], None],
    errors: list[dict[str, Any]],
) -> Mapping[str, Any] | None:
    try:
        api_client.cancel(operation_id)
        return _wait_for_terminal_and_gate(
            api_client,
            operation_id,
            timeout_seconds,
            timeline,
            monotonic,
            sleep,
            require_gate=True,
        )
    except Exception as exception:
        errors.append(
            {
                "code": getattr(exception, "code", "cancelFailed"),
                "message": str(exception),
            }
        )
        return None


def _recover_gate_satisfied(status: Mapping[str, Any] | None) -> bool:
    return bool(
        isinstance(status, Mapping)
        and status.get("status") in TERMINAL_STATUSES
        and status.get("inputOwner") == "None"
        and status.get("inputControlReleased") is True
        and status.get("canGameDriverRecover") is True
    )


def _parse_utc_timestamp(value: object) -> datetime:
    if not isinstance(value, str) or not value:
        raise OrchestrationError(
            "operationAdoptionRejected", "operation adoption timestamp is missing"
        )
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise OrchestrationError(
            "operationAdoptionRejected", "operation adoption timestamp is invalid"
        ) from exception
    if parsed.tzinfo is None:
        raise OrchestrationError(
            "operationAdoptionRejected", "operation adoption timestamp must include UTC offset"
        )
    return parsed.astimezone(timezone.utc)


def _assertion_report(
    predicate_id: str, state: _AssertionState
) -> dict[str, Any]:
    return {
        "predicateId": predicate_id,
        "evaluableSampleCount": state.evaluable_count,
        "matched": state.matched,
        "lastReason": state.last_reason,
        "evidence": state.evidence,
    }


def _write_journal(
    context: SessionContext,
    operation_id: str,
    phase: str,
    status_timeline: Sequence[Mapping[str, Any]],
    observations: Sequence[Mapping[str, Any]],
    next_log_sequence: int,
    log_pages: Sequence[Mapping[str, Any]],
    text_redactor: Callable[[str], str],
) -> None:
    atomic_write_json(
        context.artifact_directory / "journal.json",
        {
            "schema": "betterbtd/script-test-journal",
            "schemaVersion": 1,
            "scenarioId": context.scenario_id,
            "runId": context.run_id,
            "operationId": operation_id,
            "phase": phase,
            "observations": observations,
            "nonOracleDiagnostics": {
                "statusTimeline": status_timeline,
                "logs": {
                    "nextSequence": next_log_sequence,
                    "pages": log_pages,
                },
            },
        },
        text_redactor=text_redactor,
    )
