from __future__ import annotations

from dataclasses import dataclass
import json
import os
from pathlib import Path
import subprocess
from typing import Any, Callable, Mapping, Sequence

from .artifacts import SessionContext, atomic_write_json, relative_artifact_path


_OBSERVATION_PHASES = frozenset({"arrange", "act", "assert", "recover"})
_SENSITIVE_ENV_FRAGMENTS = (
    "authorization",
    "credential",
    "password",
    "private_key",
    "secret",
    "token",
)


class GameDriverClientError(RuntimeError):
    pass


@dataclass(frozen=True)
class GameDriverObservation:
    phase: str
    index: int
    directory: Path
    evidence_path: Path
    recognition_path: Path
    capture: Mapping[str, Any]
    interpretation: Mapping[str, Any]

    @property
    def recognition(self) -> Mapping[str, Any]:
        value = self.interpretation.get("recognition")
        return value if isinstance(value, Mapping) else {}

    def reference(self, context: SessionContext) -> dict[str, Any]:
        recognition = self.recognition
        page = recognition.get("page")
        page_value = page if isinstance(page, Mapping) else {}
        return {
            "phase": self.phase,
            "index": self.index,
            "evidence": relative_artifact_path(context, self.evidence_path),
            "recognition": relative_artifact_path(context, self.recognition_path),
            "capturedAtUtc": self.capture.get("capturedAtUtc"),
            "status": recognition.get("status"),
            "oracleEligible": recognition.get("oracleEligible") is True,
            "pageId": page_value.get("id"),
        }


class GameDriverClient:
    def __init__(
        self,
        repository_root: Path,
        *,
        process_runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
        command_timeout_seconds: float = 60.0,
        secrets_to_scrub: Sequence[str] = (),
    ) -> None:
        self._repository_root = repository_root.resolve()
        self._wrapper = (
            self._repository_root
            / "tools"
            / "BetterBTD.GameDriver"
            / "game-driver.ps1"
        )
        self._process_runner = process_runner
        self._command_timeout_seconds = command_timeout_seconds
        self._secrets_to_scrub = tuple(secret for secret in secrets_to_scrub if secret)

    def validate_catalog(self) -> Mapping[str, Any]:
        return self._run_json(("catalog",))

    def observe(
        self,
        context: SessionContext,
        *,
        phase: str,
        index: int,
        window_handle: str | None = None,
        activate: bool = False,
    ) -> GameDriverObservation:
        if phase not in _OBSERVATION_PHASES:
            raise GameDriverClientError(f"unsupported observation phase: {phase}")
        if index < 0:
            raise GameDriverClientError("observation index must be non-negative")
        if phase in ("act", "assert") and activate:
            raise GameDriverClientError(
                f"Game Driver activation is forbidden during {phase.title()}"
            )

        directory = context.artifact_directory / "observations" / phase / f"{index:06d}"
        image_path = directory / "evidence.png"
        evidence_path = directory / "evidence.json"
        recognition_path = directory / "recognition.json"
        if directory.exists():
            raise GameDriverClientError(f"observation directory already exists: {directory}")
        directory.mkdir(parents=True)

        selector = (
            ("--window-handle", window_handle) if window_handle is not None else ()
        )
        capture_arguments = ["capture", "--output", str(image_path), *selector]
        if not activate:
            capture_arguments.append("--no-activate")
        capture = self._run_json(tuple(capture_arguments))
        interpretation = self._run_json(("recognize", "--evidence", str(evidence_path)))
        atomic_write_json(recognition_path, interpretation)
        return GameDriverObservation(
            phase=phase,
            index=index,
            directory=directory,
            evidence_path=evidence_path,
            recognition_path=recognition_path,
            capture=capture,
            interpretation=interpretation,
        )

    def _run_json(self, arguments: Sequence[str]) -> Mapping[str, Any]:
        if not self._wrapper.is_file():
            raise GameDriverClientError(
                f"Game Driver wrapper does not exist: {self._wrapper}"
            )
        command = [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(self._wrapper),
            *arguments,
        ]
        environment = {
            key: value
            for key, value in os.environ.items()
            if not any(
                fragment in key.lower() for fragment in _SENSITIVE_ENV_FRAGMENTS
            )
            and not any(secret in value for secret in self._secrets_to_scrub)
        }
        try:
            result = self._process_runner(
                command,
                cwd=self._repository_root,
                env=environment,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=self._command_timeout_seconds,
                check=False,
            )
        except (OSError, subprocess.SubprocessError) as exception:
            raise GameDriverClientError(
                f"Game Driver command could not run: {exception}"
            ) from exception
        if result.returncode != 0:
            detail = result.stderr.strip() or result.stdout.strip() or "no error output"
            raise GameDriverClientError(
                f"Game Driver command failed with exit code {result.returncode}: {detail}"
            )
        try:
            value = json.loads(result.stdout)
        except json.JSONDecodeError as exception:
            raise GameDriverClientError(
                "Game Driver command did not return one JSON object"
            ) from exception
        if not isinstance(value, dict):
            raise GameDriverClientError("Game Driver JSON root must be an object")
        return value


def next_observation_index(context: SessionContext, phase: str) -> int:
    if phase not in _OBSERVATION_PHASES:
        raise GameDriverClientError(f"unsupported observation phase: {phase}")
    phase_directory = context.artifact_directory / "observations" / phase
    index = 0
    while (phase_directory / f"{index:06d}").exists():
        index += 1
    return index


def observation_window_handle(observation: GameDriverObservation) -> str:
    window = observation.capture.get("window")
    if not isinstance(window, Mapping):
        raise GameDriverClientError("capture result has no window object")
    handle = window.get("handle")
    if not isinstance(handle, str) or not handle:
        raise GameDriverClientError("capture result has no stable window handle")
    return handle
