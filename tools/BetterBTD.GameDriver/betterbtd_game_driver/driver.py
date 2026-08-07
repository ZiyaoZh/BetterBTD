from __future__ import annotations

import hashlib
import json
import os
import subprocess
import time
import uuid
from dataclasses import dataclass, replace
from datetime import datetime, timezone
from pathlib import Path

from .coordinates import coordinate_metadata
from .errors import GameDriverError, UsageError
from .models import WindowSelector, WindowSnapshot
from .png import analyze_bgrx32, encode_bgrx32, visible_pixel_sha256
from .win32 import WindowApi, capture_desktop_rect


DEFAULT_PROCESS_NAMES = ("BloonsTD6", "BloonsTD6-Epic")
DEFAULT_WINDOW_TITLES = ("BloonsTD6", "BloonsTD6-Epic")


@dataclass(frozen=True, slots=True)
class CaptureRequest:
    selector: WindowSelector
    output_path: Path | None
    launch_path: Path | None
    activate: bool
    overwrite: bool
    settle_ms: int
    activation_timeout_ms: int
    window_timeout_ms: int
    launch_timeout_ms: int


class GameDriver:
    def __init__(self, window_api: WindowApi | None = None) -> None:
        self._window_api = window_api or WindowApi()

    def list_windows(
        self,
        selector: WindowSelector,
        *,
        include_all: bool = False,
    ) -> dict[str, object]:
        observed_at = _utc_now()
        windows = self._window_api.list_windows(selector, include_all=include_all)
        return {
            "schemaVersion": 1,
            "observedAtUtc": _format_timestamp(observed_at),
            "coordinateSystem": "screenPhysicalPixels",
            "windows": [window.to_dict() for window in windows],
        }

    def capture(self, request: CaptureRequest) -> dict[str, object]:
        window, launched = self._resolve_window(request)
        output_path = resolve_output_path(request.output_path, window.process_id, _utc_now())
        metadata_path = output_path.with_suffix(".json")
        completion_path = metadata_path.with_name(f"{metadata_path.stem}.complete.json")
        lock_path = metadata_path.with_name(f"{metadata_path.stem}.lock")
        _validate_output_paths(
            output_path,
            metadata_path,
            completion_path,
            lock_path,
            request.overwrite,
        )

        compositor_flushed = False
        if request.activate:
            activated = self._window_api.activate(
                window.handle, request.activation_timeout_ms / 1000
            )
            if not activated:
                raise GameDriverError(
                    "activationFailed",
                    "BTD6 could not be verified as the foreground window; no screenshot was taken.",
                    4,
                )
            compositor_flushed = self._window_api.flush_compositor()
        elif window.minimized:
            raise GameDriverError(
                "windowMinimized",
                "The target window is minimized. Capture with activation enabled or restore it first.",
                4,
            )

        if request.settle_ms > 0:
            time.sleep(request.settle_ms / 1000)

        before_capture = self._window_api.snapshot(window.handle)
        if before_capture.minimized or not before_capture.visible:
            raise GameDriverError(
                "windowNotVisible",
                "The target window is not visible after the settle interval.",
                4,
            )
        if request.activate and not before_capture.foreground:
            raise GameDriverError(
                "foregroundLost",
                "BTD6 lost foreground ownership before capture; no screenshot was taken.",
                4,
            )

        virtual_screen = self._window_api.virtual_screen_rect()
        if not virtual_screen.contains(before_capture.client_rect):
            raise GameDriverError(
                "clientOutsideVirtualScreen",
                "The complete BTD6 client area is not inside the virtual desktop; move the window fully on-screen.",
                5,
            )

        capture_started = _utc_now()
        capture_clock_started = time.perf_counter()
        pixels = capture_desktop_rect(before_capture.client_rect)
        capture_duration_ms = round((time.perf_counter() - capture_clock_started) * 1000, 3)
        captured_at = _utc_now()
        after_capture = self._window_api.snapshot(window.handle)
        _validate_window_after_capture(before_capture, after_capture, request.activate)

        png_bytes = encode_bgrx32(
            before_capture.client_rect.width,
            before_capture.client_rect.height,
            pixels,
        )
        pixel_hash = visible_pixel_sha256(
            before_capture.client_rect.width,
            before_capture.client_rect.height,
            pixels,
        )
        file_hash = hashlib.sha256(png_bytes).hexdigest()
        analysis = analyze_bgrx32(
            before_capture.client_rect.width,
            before_capture.client_rect.height,
            pixels,
        )

        warnings: list[dict[str, str]] = []
        if not request.activate and not before_capture.foreground:
            warnings.append(
                {
                    "code": "foregroundNotVerified",
                    "message": "Capture was requested without activation; another window may occlude the game.",
                }
            )
        if request.activate and not compositor_flushed:
            warnings.append(
                {
                    "code": "compositorFlushFailed",
                    "message": "DwmFlush did not succeed before the settle interval.",
                }
            )
        if analysis["isNearBlack"]:
            warnings.append(
                {
                    "code": "nearBlackFrame",
                    "message": "The captured frame is nearly black and should be visually inspected.",
                }
            )
        elif analysis["isUniform"]:
            warnings.append(
                {
                    "code": "uniformFrame",
                    "message": "The captured frame contains one uniform color and should be visually inspected.",
                }
            )

        evidence_id = uuid.uuid4().hex
        evidence = {
            "schemaVersion": 1,
            "evidenceId": evidence_id,
            "evidenceRole": "rawExternalObservation",
            "source": "BetterBTD.GameDriver",
            "capturedAtUtc": _format_timestamp(captured_at),
            "capture": {
                "backend": "desktop-gdi-bitblt",
                "startedAtUtc": _format_timestamp(capture_started),
                "durationMs": capture_duration_ms,
                "settleMs": request.settle_ms,
                "dwmFlushSucceeded": compositor_flushed if request.activate else None,
                "occlusionSensitive": True,
                "requiresVisibleDesktop": True,
                "stabilityCheckPerformed": False,
            },
            "window": before_capture.to_dict(),
            "windowAfterCapture": {
                "visible": after_capture.visible,
                "minimized": after_capture.minimized,
                "foreground": after_capture.foreground,
            },
            "launch": {"startedByDriver": launched},
            "coordinateSystem": {
                "id": "clientPhysicalPixels",
                "origin": "clientTopLeft",
                "xAxis": "right",
                "yAxis": "down",
                "bounds": {
                    "xMinimum": 0,
                    "yMinimum": 0,
                    "xMaximumExclusive": before_capture.client_rect.width,
                    "yMaximumExclusive": before_capture.client_rect.height,
                },
                "clientOriginOnScreen": {
                    "x": before_capture.client_rect.x,
                    "y": before_capture.client_rect.y,
                },
                "dpiAwareness": "PerMonitorV2",
                **coordinate_metadata(
                    before_capture.client_rect.width,
                    before_capture.client_rect.height,
                ),
            },
            "frameFingerprint": {
                "algorithm": "sha256(width-le32|height-le32|rgb24-top-down)",
                "value": pixel_hash,
            },
            "frameAnalysis": analysis,
            "files": {
                "image": {
                    "path": str(output_path),
                    "format": "png-rgb8",
                    "sourcePixelFormat": "bgrx32-top-down",
                    "bytes": len(png_bytes),
                    "sha256": file_hash,
                },
                "metadata": {"path": str(metadata_path)},
                "completion": {
                    "path": str(completion_path),
                    "required": True,
                    "protocol": "evidence-commit-marker-v1",
                },
            },
            "warnings": warnings,
        }

        _write_evidence(
            output_path,
            metadata_path,
            completion_path,
            lock_path,
            png_bytes,
            evidence,
            evidence_id,
            request.overwrite,
        )
        return evidence

    def _resolve_window(self, request: CaptureRequest) -> tuple[WindowSnapshot, bool]:
        candidate = self._wait_for_one(request.selector, request.window_timeout_ms)
        if candidate is not None:
            return candidate, False
        if request.launch_path is None:
            raise GameDriverError(
                "windowNotFound",
                "No BTD6 window matched the requested selector.",
                3,
            )

        launch_path = request.launch_path.expanduser().resolve()
        if not launch_path.is_file():
            raise GameDriverError(
                "launchPathNotFound",
                f"The launch executable does not exist: {launch_path}",
                3,
            )

        try:
            subprocess.Popen(
                [str(launch_path)],
                cwd=str(launch_path.parent),
                close_fds=True,
            )
        except OSError as error:
            raise GameDriverError(
                "launchFailed",
                f"Could not start BTD6 from {launch_path}: {error}",
                3,
            ) from error

        selector = request.selector
        if not selector.process_names and selector.process_id is None and selector.handle is None:
            selector = replace(selector, process_names=(launch_path.stem,))
        candidate = self._wait_for_one(selector, request.launch_timeout_ms)
        if candidate is None:
            raise GameDriverError(
                "windowLaunchTimeout",
                f"BTD6 was launched but no matching window appeared within {request.launch_timeout_ms} ms.",
                3,
            )
        return candidate, True

    def _wait_for_one(
        self,
        selector: WindowSelector,
        timeout_ms: int,
    ) -> WindowSnapshot | None:
        deadline = time.monotonic() + timeout_ms / 1000
        while True:
            candidates = self._window_api.list_windows(selector)
            if len(candidates) > 1:
                handles = ", ".join(f"0x{item.handle:016X}" for item in candidates)
                raise GameDriverError(
                    "ambiguousWindow",
                    f"Multiple windows matched ({handles}); select one with --window-handle.",
                    3,
                )
            if candidates:
                return candidates[0]
            if time.monotonic() >= deadline:
                return None
            time.sleep(0.2)


def resolve_output_path(
    requested_path: Path | None,
    process_id: int,
    timestamp: datetime,
) -> Path:
    if requested_path is not None:
        path = requested_path.expanduser()
        if path.suffix.casefold() != ".png":
            raise UsageError("--output must name a .png file.")
        return path.resolve()

    day = timestamp.strftime("%Y%m%d")
    instant = timestamp.strftime("%H%M%S.%f")[:10] + "Z"
    return (
        Path.cwd()
        / "artifacts"
        / "game-driver"
        / day
        / f"capture-{instant}-{process_id}.png"
    ).resolve()


def _validate_output_paths(
    image_path: Path,
    metadata_path: Path,
    completion_path: Path,
    lock_path: Path,
    overwrite: bool,
) -> None:
    if lock_path.exists():
        raise GameDriverError(
            "outputBusy",
            f"Another evidence write is active or was interrupted: {lock_path}",
            5,
        )
    if overwrite:
        return
    existing = [
        path
        for path in (image_path, metadata_path, completion_path)
        if path.exists()
    ]
    if existing:
        formatted = ", ".join(str(path) for path in existing)
        raise GameDriverError(
            "outputExists",
            f"Evidence output already exists: {formatted}. Use --overwrite to replace it.",
            5,
        )


def _write_evidence(
    image_path: Path,
    metadata_path: Path,
    completion_path: Path,
    lock_path: Path,
    png_bytes: bytes,
    evidence: dict[str, object],
    evidence_id: str,
    overwrite: bool,
) -> None:
    image_path.parent.mkdir(parents=True, exist_ok=True)
    metadata_bytes = (
        json.dumps(evidence, ensure_ascii=False, indent=2) + os.linesep
    ).encode("utf-8")
    completion_bytes = (
        json.dumps(
            {
                "schemaVersion": 1,
                "protocol": "evidence-commit-marker-v1",
                "evidenceId": evidence_id,
                "imageSha256": hashlib.sha256(png_bytes).hexdigest(),
                "metadataSha256": hashlib.sha256(metadata_bytes).hexdigest(),
            },
            ensure_ascii=False,
            indent=2,
        )
        + os.linesep
    ).encode("utf-8")
    temporary_suffix = f".{uuid.uuid4().hex}.tmp"
    temporary_image = image_path.with_name(image_path.name + temporary_suffix)
    temporary_metadata = metadata_path.with_name(metadata_path.name + temporary_suffix)
    temporary_completion = completion_path.with_name(
        completion_path.name + temporary_suffix
    )

    lock_descriptor: int | None = None
    lock_acquired = False
    try:
        try:
            lock_descriptor = os.open(
                lock_path,
                os.O_CREAT | os.O_EXCL | os.O_WRONLY,
            )
        except FileExistsError as error:
            raise GameDriverError(
                "outputBusy",
                f"Another evidence write is active or was interrupted: {lock_path}",
                5,
            ) from error
        lock_acquired = True
        os.write(lock_descriptor, evidence_id.encode("ascii"))
        os.close(lock_descriptor)
        lock_descriptor = None

        if not overwrite:
            existing = [
                path
                for path in (image_path, metadata_path, completion_path)
                if path.exists()
            ]
            if existing:
                formatted = ", ".join(str(path) for path in existing)
                raise GameDriverError(
                    "outputExists",
                    f"Evidence output already exists: {formatted}. Use --overwrite to replace it.",
                    5,
                )

        temporary_image.write_bytes(png_bytes)
        temporary_metadata.write_bytes(metadata_bytes)
        temporary_completion.write_bytes(completion_bytes)

        if overwrite:
            completion_path.unlink(missing_ok=True)
            os.replace(temporary_image, image_path)
            os.replace(temporary_metadata, metadata_path)
            os.replace(temporary_completion, completion_path)
        else:
            os.rename(temporary_image, image_path)
            os.rename(temporary_metadata, metadata_path)
            os.rename(temporary_completion, completion_path)
    finally:
        if lock_descriptor is not None:
            os.close(lock_descriptor)
        if lock_acquired:
            lock_path.unlink(missing_ok=True)
        temporary_image.unlink(missing_ok=True)
        temporary_metadata.unlink(missing_ok=True)
        temporary_completion.unlink(missing_ok=True)


def _validate_window_after_capture(
    before_capture: WindowSnapshot,
    after_capture: WindowSnapshot,
    require_foreground: bool,
) -> None:
    changes: list[str] = []
    if after_capture.process_id != before_capture.process_id:
        changes.append("processId")
    if after_capture.client_rect != before_capture.client_rect:
        changes.append("clientRect")
    if after_capture.dpi != before_capture.dpi:
        changes.append("dpi")
    if not after_capture.visible:
        changes.append("visibility")
    if after_capture.minimized:
        changes.append("minimized")
    if require_foreground and not after_capture.foreground:
        changes.append("foreground")

    if changes:
        changed_fields = ", ".join(changes)
        raise GameDriverError(
            "windowChangedDuringCapture",
            f"The target window changed during capture ({changed_fields}); no evidence was saved.",
            5,
        )


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _format_timestamp(timestamp: datetime) -> str:
    return timestamp.isoformat(timespec="milliseconds").replace("+00:00", "Z")
