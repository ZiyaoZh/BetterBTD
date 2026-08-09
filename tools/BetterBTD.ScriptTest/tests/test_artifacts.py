from __future__ import annotations

from datetime import datetime, timezone
import json
from pathlib import Path
import re
import tempfile
import unittest
from uuid import uuid4

from betterbtd_script_test.artifacts import (
    SessionError,
    atomic_write_json,
    create_session,
    load_session,
    new_run_id,
    redact,
)


TOOL_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOL_ROOT.parents[1]
EXAMPLE_PATH = TOOL_ROOT / "examples" / "easy-standard-victory.scenario.json"


class ArtifactTests(unittest.TestCase):
    def test_new_run_id_is_protocol_safe(self) -> None:
        run_id = new_run_id(datetime(2026, 8, 9, 12, 34, 56, 789000, timezone.utc))

        self.assertRegex(run_id, r"^20260809t123456\.789z-[a-f0-9]{8}$")

    def test_redact_removes_sensitive_keys_bearers_and_exact_secrets(self) -> None:
        value = {
            "token": "top-secret",
            "nested": [
                "Authorization: Bearer abc123",
                "prefix top-secret suffix",
            ],
            "expectedSha256": "allowed",
        }

        result = redact(value, ("top-secret",))

        self.assertEqual("[REDACTED]", result["token"])
        self.assertEqual("Authorization: Bearer [REDACTED]", result["nested"][0])
        self.assertEqual("prefix [REDACTED] suffix", result["nested"][1])
        self.assertEqual("allowed", result["expectedSha256"])

    def test_redact_applies_client_owned_text_redactor_recursively(self) -> None:
        result = redact(
            {"logs": ["contains opaque-value"]},
            text_redactor=lambda value: value.replace("opaque-value", "[CLIENT]"),
        )

        self.assertEqual("contains [CLIENT]", result["logs"][0])

    def test_atomic_write_json_replaces_complete_document(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "report.json"
            atomic_write_json(path, {"value": 1})
            atomic_write_json(path, {"value": 2})

            document = json.loads(path.read_text(encoding="utf-8"))

        self.assertEqual({"value": 2}, document)

    def test_create_and_load_session_binds_scenario_hash(self) -> None:
        with self._scenario() as scenario_path:
            document = json.loads(scenario_path.read_text(encoding="utf-8"))
            run_id = f"unit-{uuid4().hex}"
            context = create_session(scenario_path, run_id=run_id)
            self.addCleanup(self._remove_artifact_directory, context.artifact_directory)

            loaded, manifest = load_session(scenario_path, run_id)

            self.assertEqual(context.artifact_directory, loaded.artifact_directory)
            self.assertEqual("Arrange", manifest["phase"])

            document["description"] = "changed after session creation"
            scenario_path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaisesRegex(SessionError, "scenarioSha256"):
                load_session(scenario_path, run_id)

    def test_invalid_run_id_is_rejected_before_creating_artifacts(self) -> None:
        with self._scenario() as scenario_path:
            with self.assertRaisesRegex(SessionError, "run ID"):
                create_session(scenario_path, run_id="../escape")

    def _scenario(self):
        return _TemporaryScenario()

    @staticmethod
    def _remove_artifact_directory(path: Path) -> None:
        if not path.exists():
            return
        for child in sorted(path.rglob("*"), reverse=True):
            if child.is_file():
                child.unlink()
            elif child.is_dir():
                child.rmdir()
        path.rmdir()


class _TemporaryScenario:
    def __init__(self) -> None:
        self._temporary: tempfile.TemporaryDirectory[str] | None = None

    def __enter__(self) -> Path:
        self._temporary = tempfile.TemporaryDirectory(
            prefix="script-test-artifacts-", dir=TOOL_ROOT
        )
        root = Path(self._temporary.name)
        script_path = root / "script.json"
        script_path.write_text("{}", encoding="utf-8")
        scenario = json.loads(EXAMPLE_PATH.read_text(encoding="utf-8"))
        scenario_id = f"unit-{uuid4().hex}"
        scenario["id"] = scenario_id
        scenario["script"]["path"] = "script.json"
        scenario["failureArtifacts"]["directory"] = (
            "artifacts/script-test-unit/{scenarioId}/{runId}"
        )
        scenario_path = root / "scenario.json"
        scenario_path.write_text(json.dumps(scenario), encoding="utf-8")
        return scenario_path

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        assert self._temporary is not None
        self._temporary.cleanup()
