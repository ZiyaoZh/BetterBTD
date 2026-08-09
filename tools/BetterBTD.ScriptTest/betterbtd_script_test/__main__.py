from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

from .scenario import CURRENT_CAPABILITIES, ScenarioValidationError, validate_scenario


def _parse_args(arguments: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate a BetterBTD script test scenario against scenario-v1."
    )
    parser.add_argument("scenario", type=Path)
    parser.add_argument(
        "--check-script-path",
        action="store_true",
        help="Require script.path to name an existing file.",
    )
    return parser.parse_args(arguments)


def main(arguments: list[str] | None = None) -> int:
    parsed = _parse_args(arguments)
    try:
        result = validate_scenario(
            parsed.scenario,
            available_capabilities=CURRENT_CAPABILITIES,
            require_script_exists=parsed.check_script_path,
        )
    except ScenarioValidationError as exception:
        print(
            json.dumps(
                {
                    "valid": False,
                    "error": {
                        "code": "scenarioInvalid",
                        "messages": list(exception.errors),
                    },
                },
                indent=2,
            ),
            file=sys.stderr,
        )
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
    print(json.dumps(response, indent=2))
    return 0 if result.capability_compatible else 3


if __name__ == "__main__":
    raise SystemExit(main())
