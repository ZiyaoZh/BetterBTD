from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys

from .api_client import TestApiClient, TestApiClientError
from .artifacts import SessionError, create_session
from .game_driver_client import GameDriverClient, GameDriverClientError
from .orchestrator import (
    OrchestrationError,
    cancel_and_gate,
    run_act_and_assert,
    verify_recover_target,
)
from .scenario import CURRENT_CAPABILITIES, ScenarioValidationError, validate_scenario


_PACKAGE_ROOT = Path(__file__).resolve().parent
_REPOSITORY_ROOT = _PACKAGE_ROOT.parents[2]
_COMMANDS = frozenset(
    {"validate", "prepare", "run-act-assert", "cancel-and-gate", "verify-recover"}
)


def _validation_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Validate a BetterBTD script test scenario against scenario-v1."
    )
    parser.add_argument("scenario", type=Path)
    parser.add_argument(
        "--check-script-path",
        action="store_true",
        help="Require script.path to name an existing file.",
    )
    return parser


def _command_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run the deterministic BetterBTD script-test phase handoffs."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    validate = subparsers.add_parser("validate", help="Validate scenario-v1.")
    validate.add_argument("scenario", type=Path)
    validate.add_argument("--check-script-path", action="store_true")

    prepare = subparsers.add_parser(
        "prepare", help="Create an auditable run session before Arrange."
    )
    prepare.add_argument("scenario", type=Path)
    prepare.add_argument("--run-id")

    run = subparsers.add_parser(
        "run-act-assert",
        help="Verify Arrange, execute through Test API, observe, assert, and gate Recover.",
    )
    run.add_argument("scenario", type=Path)
    run.add_argument("--run-id", required=True)
    run.add_argument("--api-url", default="http://127.0.0.1:18767/")
    run.add_argument("--token-env", default="BETTERBTD_TEST_API_TOKEN")

    cancel = subparsers.add_parser(
        "cancel-and-gate",
        help="Cancel an interrupted run and poll until the Recover gate is explicit.",
    )
    cancel.add_argument("scenario", type=Path)
    cancel.add_argument("--run-id", required=True)
    cancel.add_argument("--api-url", default="http://127.0.0.1:18767/")
    cancel.add_argument("--token-env", default="BETTERBTD_TEST_API_TOKEN")

    recover = subparsers.add_parser(
        "verify-recover",
        help="Verify the scenario Recover target after authorized Agent input.",
    )
    recover.add_argument("scenario", type=Path)
    recover.add_argument("--run-id", required=True)
    return parser


def main(arguments: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if arguments is None else arguments)
    if not argv or argv[0] not in _COMMANDS:
        parsed = _validation_parser().parse_args(argv)
        return _run_validation(parsed.scenario, parsed.check_script_path)

    parsed = _command_parser().parse_args(argv)
    if parsed.command == "validate":
        return _run_validation(parsed.scenario, parsed.check_script_path)
    try:
        if parsed.command == "prepare":
            context = create_session(parsed.scenario, run_id=parsed.run_id)
            _write_json(
                {
                    "prepared": True,
                    "scenarioId": context.scenario_id,
                    "runId": context.run_id,
                    "artifactDirectory": str(context.artifact_directory),
                    "manifestPath": str(context.manifest_path),
                    "phase": "Arrange",
                }
            )
            return 0
        if parsed.command == "run-act-assert":
            token = os.environ.get(parsed.token_env)
            if token is None:
                raise OrchestrationError(
                    "testApiTokenMissing",
                    f"Test API token environment variable is not set: {parsed.token_env}",
                )
            api_client = TestApiClient(parsed.api_url, token)
            outcome = run_act_and_assert(
                parsed.scenario,
                parsed.run_id,
                api_client,
                GameDriverClient(_REPOSITORY_ROOT, secrets_to_scrub=(token,)),
            )
            _write_json(
                {
                    "result": outcome.result,
                    "recoverAuthorized": outcome.recover_authorized,
                    "operationId": outcome.operation_id,
                    "reportPath": str(outcome.report_path),
                }
            )
            return 0 if outcome.result == "Passed" else (1 if outcome.result == "Failed" else 3)
        if parsed.command == "cancel-and-gate":
            token = os.environ.get(parsed.token_env)
            if token is None:
                raise OrchestrationError(
                    "testApiTokenMissing",
                    f"Test API token environment variable is not set: {parsed.token_env}",
                )
            outcome = cancel_and_gate(
                parsed.scenario,
                parsed.run_id,
                TestApiClient(parsed.api_url, token),
            )
            _write_json(
                {
                    "result": outcome.result,
                    "recoverAuthorized": outcome.recover_authorized,
                    "operationId": outcome.operation_id,
                    "reportPath": str(outcome.report_path),
                }
            )
            return 3
        if parsed.command == "verify-recover":
            result = verify_recover_target(
                parsed.scenario,
                parsed.run_id,
                GameDriverClient(_REPOSITORY_ROOT),
            )
            _write_json(result)
            return 0
        raise OrchestrationError("unsupportedCommand", parsed.command)
    except (
        GameDriverClientError,
        OrchestrationError,
        SessionError,
        TestApiClientError,
        ValueError,
    ) as exception:
        _write_error(
            getattr(exception, "code", "scriptTestError"),
            str(exception),
        )
        return 3


def _run_validation(scenario: Path, check_script_path: bool) -> int:
    try:
        result = validate_scenario(
            scenario,
            available_capabilities=CURRENT_CAPABILITIES,
            require_script_exists=check_script_path,
        )
    except ScenarioValidationError as exception:
        _write_error("scenarioInvalid", list(exception.errors))
        return 2

    response = {
        "valid": True,
        "capabilityCompatible": result.capability_compatible,
        "scenarioPath": str(result.scenario_path),
        "scriptPath": str(result.script_path),
        "artifactDirectory": str(result.artifact_directory),
        "requiredCapabilities": sorted(result.required_capabilities),
        "availableCapabilities": sorted(result.available_capabilities),
        "missingCapabilities": sorted(result.missing_capabilities),
    }
    _write_json(response)
    return 0 if result.capability_compatible else 3


def _write_json(value: object, *, stream=None) -> None:
    print(
        json.dumps(value, ensure_ascii=False, indent=2),
        file=sys.stdout if stream is None else stream,
    )


def _write_error(code: str, message: object) -> None:
    _write_json(
        {"valid": False, "error": {"code": code, "messages": message}},
        stream=sys.stderr,
    )


if __name__ == "__main__":
    raise SystemExit(main())
