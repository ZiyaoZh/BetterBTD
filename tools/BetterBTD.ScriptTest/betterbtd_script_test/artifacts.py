from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
from typing import Any, Callable, Mapping

from .scenario import (
    ScenarioValidationError,
    ScenarioValidationResult,
    load_scenario_document,
    validate_scenario,
)


SESSION_SCHEMA = "betterbtd/script-test-session"
SESSION_SCHEMA_VERSION = 1
RUN_ID_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._-]{0,127}$")
_SENSITIVE_KEY_FRAGMENTS = (
    "accesskey",
    "apikey",
    "authorization",
    "bearer",
    "credential",
    "password",
    "privatekey",
    "secret",
    "token",
)
_BEARER_PATTERN = re.compile(r"(?i)\bbearer\s+[^\s,;]+")


class SessionError(RuntimeError):
    pass


@dataclass(frozen=True)
class SessionContext:
    scenario_path: Path
    script_path: Path
    scenario_id: str
    run_id: str
    artifact_directory: Path
    manifest_path: Path
    document: Mapping[str, Any]
    validation: ScenarioValidationResult


def new_run_id(now: datetime | None = None) -> str:
    timestamp = (now or datetime.now(timezone.utc)).astimezone(timezone.utc)
    return timestamp.strftime("%Y%m%dt%H%M%S.%f")[:-3] + "z-" + secrets.token_hex(4)


def create_session(
    scenario_path: Path | str,
    *,
    run_id: str | None = None,
) -> SessionContext:
    context = _resolve_context(scenario_path, run_id or new_run_id())
    if context.artifact_directory.exists():
        raise SessionError(
            f"artifact directory already exists: {context.artifact_directory}"
        )

    context.artifact_directory.mkdir(parents=True)
    manifest = {
        "schema": SESSION_SCHEMA,
        "schemaVersion": SESSION_SCHEMA_VERSION,
        "scenarioId": context.scenario_id,
        "runId": context.run_id,
        "scenarioPath": str(context.scenario_path),
        "scenarioSha256": _sha256_file(context.scenario_path),
        "scriptPath": str(context.script_path),
        "createdAtUtc": _utc_now(),
        "phase": "Arrange",
        "operationId": None,
        "recoverAuthorized": False,
    }
    atomic_write_json(context.manifest_path, manifest)
    return context


def load_session(
    scenario_path: Path | str,
    run_id: str,
) -> tuple[SessionContext, dict[str, Any]]:
    context = _resolve_context(scenario_path, run_id)
    try:
        manifest = _read_json_object(context.manifest_path)
    except OSError as exception:
        raise SessionError(
            f"cannot read session manifest {context.manifest_path}: {exception}"
        ) from exception
    except (json.JSONDecodeError, ValueError) as exception:
        raise SessionError(
            f"cannot parse session manifest {context.manifest_path}: {exception}"
        ) from exception

    expected = {
        "schema": SESSION_SCHEMA,
        "schemaVersion": SESSION_SCHEMA_VERSION,
        "scenarioId": context.scenario_id,
        "runId": context.run_id,
        "scenarioPath": str(context.scenario_path),
        "scenarioSha256": _sha256_file(context.scenario_path),
        "scriptPath": str(context.script_path),
    }
    mismatches = [
        f"{field} is {manifest.get(field)!r}, expected {value!r}"
        for field, value in expected.items()
        if manifest.get(field) != value
    ]
    if mismatches:
        raise SessionError("session manifest mismatch: " + "; ".join(mismatches))
    return context, manifest


def update_manifest(
    context: SessionContext,
    manifest: Mapping[str, Any],
    **changes: Any,
) -> dict[str, Any]:
    updated = dict(manifest)
    updated.update(changes)
    updated["updatedAtUtc"] = _utc_now()
    atomic_write_json(context.manifest_path, updated)
    return updated


def atomic_write_json(
    path: Path,
    value: Any,
    *,
    secrets_to_redact: tuple[str, ...] = (),
    text_redactor: Callable[[str], str] | None = None,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    sanitized = redact(value, secrets_to_redact, text_redactor=text_redactor)
    temporary_path = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    try:
        with temporary_path.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(sanitized, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
    finally:
        try:
            temporary_path.unlink(missing_ok=True)
        except OSError:
            pass


def redact(
    value: Any,
    secrets_to_redact: tuple[str, ...] = (),
    *,
    text_redactor: Callable[[str], str] | None = None,
) -> Any:
    secrets_present = tuple(secret for secret in secrets_to_redact if secret)
    if isinstance(value, Mapping):
        result: dict[str, Any] = {}
        for key, item in value.items():
            normalized_key = re.sub(r"[^a-z0-9]", "", str(key).lower())
            if any(fragment in normalized_key for fragment in _SENSITIVE_KEY_FRAGMENTS):
                result[str(key)] = "[REDACTED]"
            else:
                result[str(key)] = redact(
                    item, secrets_present, text_redactor=text_redactor
                )
        return result
    if isinstance(value, (list, tuple)):
        return [
            redact(item, secrets_present, text_redactor=text_redactor)
            for item in value
        ]
    if isinstance(value, str):
        sanitized = _BEARER_PATTERN.sub("Bearer [REDACTED]", value)
        for secret in secrets_present:
            sanitized = sanitized.replace(secret, "[REDACTED]")
        if text_redactor is not None:
            sanitized = text_redactor(sanitized)
        return sanitized
    return value


def relative_artifact_path(context: SessionContext, path: Path) -> str:
    resolved = path.resolve()
    try:
        relative = resolved.relative_to(context.artifact_directory)
    except ValueError as exception:
        raise SessionError(f"artifact path escapes the run directory: {path}") from exception
    return relative.as_posix()


def _resolve_context(scenario_path: Path | str, run_id: str) -> SessionContext:
    if not RUN_ID_PATTERN.fullmatch(run_id):
        raise SessionError(
            "run ID must match [a-z0-9][a-z0-9._-]{0,127}"
        )
    try:
        validation = validate_scenario(scenario_path, require_script_exists=True)
    except ScenarioValidationError as exception:
        raise SessionError(str(exception)) from exception
    if not validation.capability_compatible:
        missing = ", ".join(sorted(validation.missing_capabilities))
        raise SessionError(f"required Oracle capabilities are unavailable: {missing}")

    document = load_scenario_document(validation.scenario_path)
    scenario_id = document["id"]
    raw_template = str(validation.artifact_directory)
    raw_directory = raw_template.replace("{scenarioId}", scenario_id).replace(
        "{runId}", run_id
    )
    artifact_directory = Path(raw_directory).resolve()
    repository_root = validation.scenario_path.parents[0]
    for candidate in validation.scenario_path.parents:
        if (candidate / ".git").exists():
            repository_root = candidate
            break
    artifacts_root = (repository_root / "artifacts").resolve()
    try:
        relative = artifact_directory.relative_to(artifacts_root)
    except ValueError as exception:
        raise SessionError("expanded artifact directory escapes artifacts/") from exception
    if not relative.parts or "{" in str(artifact_directory) or "}" in str(artifact_directory):
        raise SessionError("expanded artifact directory is invalid")

    return SessionContext(
        scenario_path=validation.scenario_path,
        script_path=validation.script_path,
        scenario_id=scenario_id,
        run_id=run_id,
        artifact_directory=artifact_directory,
        manifest_path=artifact_directory / "session.json",
        document=document,
        validation=validation,
    )


def _read_json_object(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError("JSON root must be an object")
    return value


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace(
        "+00:00", "Z"
    )
