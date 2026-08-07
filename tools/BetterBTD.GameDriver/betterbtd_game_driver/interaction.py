from __future__ import annotations

import json
import os
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from io import BytesIO
from pathlib import Path
from typing import Callable

from PIL import Image, ImageChops, ImageStat

from .coordinates import reference_to_client
from .driver import CaptureRequest, GameDriver
from .evidence import EvidenceBundle, evidence_reference, read_evidence
from .errors import GameDriverError, UsageError
from .models import Rect, WindowSelector, WindowSnapshot
from .png import visible_pixel_sha256
from .vision import PageMatch, recognize_image
from .visual_catalog import VisualCatalog, VisualElement
from .win32 import capture_desktop_rect


TRANSITION_COMPARISON_SIZE = (160, 90)


@dataclass(frozen=True, slots=True)
class ClickRequest:
    selector: WindowSelector
    element_id: str
    phase: str
    output_directory: Path | None
    launch_path: Path | None
    overwrite: bool
    expected_page_id: str | None
    settle_ms: int
    activation_timeout_ms: int
    window_timeout_ms: int
    launch_timeout_ms: int
    transition_timeout_ms: int
    poll_interval_ms: int
    stable_sample_count: int
    change_threshold: float
    stability_threshold: float


class VisualTransitionTracker:
    def __init__(
        self,
        before: Image.Image,
        *,
        change_threshold: float,
        stability_threshold: float,
        stable_sample_count: int,
    ) -> None:
        self._before = _normalize_transition_image(before)
        self._previous = self._before
        self._change_threshold = change_threshold
        self._stability_threshold = stability_threshold
        self._stable_sample_count = stable_sample_count
        self.changed = False
        self.stable_samples = 0

    def observe(
        self,
        frame: Image.Image,
        *,
        elapsed_ms: int,
        fingerprint: str,
    ) -> tuple[dict[str, object], bool]:
        normalized = _normalize_transition_image(frame)
        difference_from_before = _image_difference(self._before, normalized)
        difference_from_previous = _image_difference(self._previous, normalized)
        if difference_from_before >= self._change_threshold:
            self.changed = True

        if self.changed and difference_from_previous <= self._stability_threshold:
            self.stable_samples += 1
        else:
            self.stable_samples = 0
        self._previous = normalized

        observation = {
            "elapsedMs": elapsed_ms,
            "frameFingerprint": fingerprint,
            "differenceFromBefore": difference_from_before,
            "differenceFromPrevious": difference_from_previous,
            "changed": self.changed,
            "consecutiveStableSamples": self.stable_samples,
        }
        return observation, self.changed and self.stable_samples >= self._stable_sample_count


class InteractionDriver:
    def __init__(
        self,
        game_driver: GameDriver | None = None,
        *,
        monotonic: Callable[[], float] = time.monotonic,
        sleep: Callable[[float], None] = time.sleep,
    ) -> None:
        self._game_driver = game_driver or GameDriver()
        self._monotonic = monotonic
        self._sleep = sleep

    def click(self, request: ClickRequest, catalog: VisualCatalog) -> dict[str, object]:
        if request.phase not in ("arrange", "recover"):
            raise UsageError("Click phase must be arrange or recover.")
        catalog_element_ids = {
            element.id for page in catalog.pages for element in page.elements
        }
        if request.element_id not in catalog_element_ids:
            raise UsageError(f"Unknown catalog element ID: {request.element_id}")
        if request.expected_page_id is not None and request.expected_page_id not in {
            page.id for page in catalog.pages
        }:
            raise UsageError(f"Unknown expected page ID: {request.expected_page_id}")

        output_directory = resolve_interaction_output_directory(
            request.output_directory,
            request.element_id,
            _utc_now(),
        )
        _validate_interaction_outputs(output_directory, request.overwrite)

        before_metadata_path = output_directory / "before.json"
        before_result = self._game_driver.capture(
            CaptureRequest(
                selector=request.selector,
                output_path=output_directory / "before.png",
                launch_path=request.launch_path,
                activate=True,
                overwrite=request.overwrite,
                settle_ms=request.settle_ms,
                activation_timeout_ms=request.activation_timeout_ms,
                window_timeout_ms=request.window_timeout_ms,
                launch_timeout_ms=request.launch_timeout_ms,
            )
        )
        before_evidence = read_evidence(before_metadata_path)
        before_recognition, before_match = recognize_image(before_evidence, catalog)
        target = _resolve_click_target(
            before_recognition,
            before_match,
            request.element_id,
            before_evidence,
        )

        window_handle = int(str(before_result["window"]["handle"]), 0)
        expected_window = _window_snapshot_from_evidence(before_result)
        if not self._game_driver.window_api.activate(
            window_handle,
            request.activation_timeout_ms / 1000,
        ):
            raise GameDriverError(
                "activationFailed",
                "BTD6 could not be verified as the foreground window; no input was sent.",
                4,
            )
        current_window = self._game_driver.window_api.snapshot(window_handle)
        _validate_interaction_window(expected_window, current_window)

        with Image.open(BytesIO(before_evidence.image_bytes)) as source:
            before_frame = source.convert("RGB")
        tracker = VisualTransitionTracker(
            before_frame,
            change_threshold=request.change_threshold,
            stability_threshold=request.stability_threshold,
            stable_sample_count=request.stable_sample_count,
        )

        action_x, action_y = reference_to_client(
            target.action_point[0],
            target.action_point[1],
            current_window.client_rect.width,
            current_window.client_rect.height,
        )
        clicked_at = _utc_now()
        screen_x, screen_y = self._game_driver.window_api.click_client_point(
            window_handle,
            action_x,
            action_y,
        )
        observations, transition_stable = self._wait_for_transition(
            window_handle,
            expected_window,
            tracker,
            request,
        )

        after_metadata_path = output_directory / "after.json"
        self._game_driver.capture(
            CaptureRequest(
                selector=WindowSelector(handle=window_handle),
                output_path=output_directory / "after.png",
                launch_path=None,
                activate=True,
                overwrite=request.overwrite,
                settle_ms=0,
                activation_timeout_ms=request.activation_timeout_ms,
                window_timeout_ms=0,
                launch_timeout_ms=request.launch_timeout_ms,
            )
        )
        after_evidence = read_evidence(after_metadata_path)
        after_recognition, _ = recognize_image(after_evidence, catalog)
        after_page_id = _recognized_page_id(after_recognition)
        expected_page_matched = (
            request.expected_page_id is None
            or after_page_id == request.expected_page_id
        )

        result: dict[str, object] = {
            "schemaVersion": 1,
            "operationRole": "independentInputTrace",
            "source": "BetterBTD.GameDriver",
            "operation": "clickElement",
            "elementId": request.element_id,
            "clickedAtUtc": _format_timestamp(clicked_at),
            "inputOwnershipPhase": request.phase,
            "input": {
                "button": "left",
                "coordinateSystem": "clientPhysicalPixels",
                "clientPoint": {"x": action_x, "y": action_y},
                "screenPoint": {"x": screen_x, "y": screen_y},
            },
            "before": {
                "evidence": evidence_reference(before_evidence),
                "recognition": _recognition_summary(before_recognition),
            },
            "transition": {
                "status": "changedStable" if transition_stable else "timeout",
                "timeoutMs": request.transition_timeout_ms,
                "pollIntervalMs": request.poll_interval_ms,
                "changeThreshold": request.change_threshold,
                "stabilityThreshold": request.stability_threshold,
                "requiredStableSamples": request.stable_sample_count,
                "observations": observations,
            },
            "after": {
                "evidence": evidence_reference(after_evidence),
                "recognition": _recognition_summary(after_recognition),
            },
            "expectation": {
                "pageId": request.expected_page_id,
                "matched": expected_page_matched,
            },
        }
        trace_path = output_directory / "operation.json"
        _write_json_atomic(trace_path, result, overwrite=request.overwrite)
        result["trace"] = {"path": str(trace_path)}

        if not transition_stable:
            raise GameDriverError(
                "visualTransitionTimeout",
                f"The frame did not become changed and stable within "
                f"{request.transition_timeout_ms} ms. Evidence: {trace_path}",
                6,
            )
        if not expected_page_matched:
            actual_page = after_page_id or "unknown"
            raise GameDriverError(
                "expectedPageNotObserved",
                f"Expected page {request.expected_page_id} after the click, found "
                f"{actual_page}. Evidence: {trace_path}",
                6,
            )
        return result

    def _wait_for_transition(
        self,
        window_handle: int,
        expected_window: WindowSnapshot,
        tracker: VisualTransitionTracker,
        request: ClickRequest,
    ) -> tuple[list[dict[str, object]], bool]:
        started = self._monotonic()
        deadline = started + request.transition_timeout_ms / 1000
        observations: list[dict[str, object]] = []
        while self._monotonic() < deadline:
            self._sleep(request.poll_interval_ms / 1000)
            snapshot = self._game_driver.window_api.snapshot(window_handle)
            _validate_interaction_window(expected_window, snapshot)
            pixels = capture_desktop_rect(snapshot.client_rect)
            frame = Image.frombytes(
                "RGB",
                (snapshot.client_rect.width, snapshot.client_rect.height),
                pixels,
                "raw",
                "BGRX",
            )
            elapsed_ms = round((self._monotonic() - started) * 1000)
            observation, completed = tracker.observe(
                frame,
                elapsed_ms=elapsed_ms,
                fingerprint=visible_pixel_sha256(
                    snapshot.client_rect.width,
                    snapshot.client_rect.height,
                    pixels,
                ),
            )
            observations.append(observation)
            if completed:
                return observations, True
        return observations, False


def resolve_interaction_output_directory(
    requested_path: Path | None,
    element_id: str,
    timestamp: datetime,
) -> Path:
    if requested_path is not None:
        return requested_path.expanduser().resolve()
    day = timestamp.strftime("%Y%m%d")
    instant = timestamp.strftime("%H%M%S.%f")[:10] + "Z"
    safe_element_id = element_id.replace(".", "-")
    return (
        Path.cwd()
        / "artifacts"
        / "game-driver"
        / day
        / f"click-{instant}-{safe_element_id}"
    ).resolve()


def _resolve_click_target(
    recognition: dict[str, object],
    page_match: PageMatch | None,
    element_id: str,
    evidence: EvidenceBundle,
) -> VisualElement:
    recognition_document = recognition["recognition"]
    if not isinstance(recognition_document, dict):
        raise GameDriverError("recognitionInvalid", "Recognition result is malformed.", 5)
    if page_match is None or not recognition_document.get("oracleEligible"):
        raise GameDriverError(
            "currentPageNotRecognized",
            "The current game page is not independently recognized and Oracle eligible; no input was sent.",
            6,
        )
    target = next(
        (element for element in page_match.page.elements if element.id == element_id),
        None,
    )
    if target is None:
        raise GameDriverError(
            "elementNotOnCurrentPage",
            f"Element {element_id} is not on recognized page {page_match.page.id}; no input was sent.",
            6,
        )
    if target.role != "button" or target.action_point is None:
        raise GameDriverError(
            "elementNotActionable",
            f"Element {element_id} is not an actionable button; no input was sent.",
            6,
        )
    if not target.anchor_ids:
        raise GameDriverError(
            "elementVisibilityNotEvaluated",
            f"Element {element_id} has no independent visibility detector; no input was sent.",
            6,
        )
    anchor_matches = {anchor.id: anchor for anchor in page_match.anchors}
    if not all(anchor_matches[anchor_id].matched for anchor_id in target.anchor_ids):
        raise GameDriverError(
            "elementNotVisible",
            f"Element {element_id} is not independently visible; no input was sent.",
            6,
        )
    if not evidence.oracle_eligible:
        raise GameDriverError(
            "evidenceNotOracleEligible",
            "The pre-input evidence is not Oracle eligible; no input was sent.",
            6,
        )
    return target


def _window_snapshot_from_evidence(evidence: dict[str, object]) -> WindowSnapshot:
    window = evidence["window"]
    if not isinstance(window, dict):
        raise GameDriverError("evidenceInvalid", "Window evidence is malformed.", 5)
    window_rect = window["windowRectOnScreen"]
    client_rect = window["clientRectOnScreen"]
    if not isinstance(window_rect, dict) or not isinstance(client_rect, dict):
        raise GameDriverError("evidenceInvalid", "Window rectangle evidence is malformed.", 5)
    return WindowSnapshot(
        handle=int(str(window["handle"]), 0),
        process_id=int(window["processId"]),
        process_name=str(window["processName"]) if window["processName"] is not None else None,
        title=str(window["title"]),
        visible=bool(window["visible"]),
        minimized=bool(window["minimized"]),
        foreground=bool(window["foreground"]),
        dpi=int(window["dpi"]),
        window_rect=_rect_from_dict(window_rect),
        client_rect=_rect_from_dict(client_rect),
    )


def _validate_interaction_window(
    expected: WindowSnapshot,
    actual: WindowSnapshot,
) -> None:
    changes: list[str] = []
    if actual.process_id != expected.process_id:
        changes.append("processId")
    if actual.client_rect != expected.client_rect:
        changes.append("clientRect")
    if actual.dpi != expected.dpi:
        changes.append("dpi")
    if not actual.visible:
        changes.append("visibility")
    if actual.minimized:
        changes.append("minimized")
    if not actual.foreground:
        changes.append("foreground")
    if changes:
        raise GameDriverError(
            "windowChangedDuringInteraction",
            f"The target window changed during interaction ({', '.join(changes)}).",
            5,
        )


def _recognition_summary(recognition: dict[str, object]) -> dict[str, object]:
    document = recognition["recognition"]
    if not isinstance(document, dict):
        raise GameDriverError("recognitionInvalid", "Recognition result is malformed.", 5)
    page = document.get("page")
    return {
        "status": document.get("status"),
        "oracleEligible": document.get("oracleEligible"),
        "page": page,
    }


def _recognized_page_id(recognition: dict[str, object]) -> str | None:
    document = recognition.get("recognition")
    page = document.get("page") if isinstance(document, dict) else None
    page_id = page.get("id") if isinstance(page, dict) else None
    return page_id if isinstance(page_id, str) else None


def _normalize_transition_image(image: Image.Image) -> Image.Image:
    return image.convert("RGB").resize(
        TRANSITION_COMPARISON_SIZE,
        Image.Resampling.LANCZOS,
    )


def _image_difference(first: Image.Image, second: Image.Image) -> float:
    difference = ImageChops.difference(first, second)
    channel_means = ImageStat.Stat(difference).mean
    value = sum(channel_means) / (len(channel_means) * 255)
    return round(value, 6)


def _validate_interaction_outputs(output_directory: Path, overwrite: bool) -> None:
    if output_directory.exists() and not output_directory.is_dir():
        raise UsageError(f"--output-dir must name a directory: {output_directory}")
    if overwrite:
        (output_directory / "after.complete.json").unlink(missing_ok=True)
        (output_directory / "operation.json").unlink(missing_ok=True)
        return
    expected_names = (
        "before.png",
        "before.json",
        "before.complete.json",
        "after.png",
        "after.json",
        "after.complete.json",
        "operation.json",
    )
    existing = [
        output_directory / name
        for name in expected_names
        if (output_directory / name).exists()
    ]
    if existing:
        raise GameDriverError(
            "outputExists",
            f"Interaction output already exists: {', '.join(str(path) for path in existing)}. "
            "Use --overwrite to replace it.",
            5,
        )


def _write_json_atomic(path: Path, value: dict[str, object], *, overwrite: bool) -> None:
    if path.exists() and not overwrite:
        raise GameDriverError(
            "outputExists",
            f"Interaction trace already exists: {path}.",
            5,
        )
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        temporary_path.write_bytes(
            (json.dumps(value, ensure_ascii=False, indent=2) + os.linesep).encode("utf-8")
        )
        if overwrite:
            os.replace(temporary_path, path)
        else:
            os.rename(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)


def _rect_from_dict(value: dict[str, object]) -> Rect:
    return Rect(
        x=int(value["x"]),
        y=int(value["y"]),
        width=int(value["width"]),
        height=int(value["height"]),
    )


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


def _format_timestamp(timestamp: datetime) -> str:
    return timestamp.isoformat(timespec="milliseconds").replace("+00:00", "Z")
