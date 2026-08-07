from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .errors import GameDriverError


@dataclass(frozen=True, slots=True)
class EvidenceBundle:
    evidence_id: str
    image_path: Path
    metadata_path: Path
    completion_path: Path
    image_sha256: str
    metadata_sha256: str
    completion_sha256: str
    image_bytes: bytes
    metadata: dict[str, Any]
    warnings: tuple[dict[str, str], ...]
    oracle_eligible: bool


def read_evidence(metadata_path: Path) -> EvidenceBundle:
    resolved_metadata_path = metadata_path.expanduser().resolve()
    if resolved_metadata_path.suffix.casefold() != ".json" or resolved_metadata_path.name.endswith(
        ".complete.json"
    ):
        raise GameDriverError(
            "invalidEvidencePath",
            "--evidence must name a capture metadata .json file, not a completion marker.",
            2,
        )

    image_path = resolved_metadata_path.with_suffix(".png")
    completion_path = resolved_metadata_path.with_name(
        f"{resolved_metadata_path.stem}.complete.json"
    )
    for label, path in (
        ("metadata", resolved_metadata_path),
        ("image", image_path),
        ("completion marker", completion_path),
    ):
        if not path.is_file():
            raise GameDriverError(
                "evidenceIncomplete",
                f"Evidence {label} does not exist: {path}",
                3,
            )

    metadata_bytes = _read_bytes(resolved_metadata_path)
    completion_bytes = _read_bytes(completion_path)
    metadata = _parse_json(metadata_bytes, resolved_metadata_path)
    completion = _parse_json(completion_bytes, completion_path)

    if metadata.get("schemaVersion") != 1:
        raise _invalid_evidence("metadata schemaVersion must be 1")
    if completion.get("schemaVersion") != 1:
        raise _invalid_evidence("completion schemaVersion must be 1")
    if completion.get("protocol") != "evidence-commit-marker-v1":
        raise _invalid_evidence("completion protocol is unsupported")
    warnings = metadata.get("warnings")
    if not isinstance(warnings, list) or not all(
        isinstance(item, dict)
        and isinstance(item.get("code"), str)
        and isinstance(item.get("message"), str)
        for item in warnings
    ):
        raise _invalid_evidence("metadata warnings must be an array of code/message objects")

    evidence_id = metadata.get("evidenceId")
    if not isinstance(evidence_id, str) or not evidence_id:
        raise _invalid_evidence("metadata evidenceId is missing")
    if completion.get("evidenceId") != evidence_id:
        raise _invalid_evidence("metadata and completion evidenceId values differ")

    image_bytes = _read_bytes(image_path)
    image_sha256 = hashlib.sha256(image_bytes).hexdigest()
    metadata_sha256 = hashlib.sha256(metadata_bytes).hexdigest()
    if completion.get("imageSha256") != image_sha256:
        raise _invalid_evidence("image SHA-256 does not match the completion marker")
    if completion.get("metadataSha256") != metadata_sha256:
        raise _invalid_evidence("metadata SHA-256 does not match the completion marker")

    files = metadata.get("files")
    if not isinstance(files, dict):
        raise _invalid_evidence("metadata files must be an object")
    image_file = files.get("image")
    if not isinstance(image_file, dict):
        raise _invalid_evidence("metadata files.image must be an object")
    if image_file.get("format") != "png-rgb8":
        raise _invalid_evidence("metadata image format must be png-rgb8")
    if image_file.get("sha256") != image_sha256:
        raise _invalid_evidence("image SHA-256 does not match capture metadata")

    verified_warnings = tuple(
        {"code": item["code"], "message": item["message"]} for item in warnings
    )
    return EvidenceBundle(
        evidence_id=evidence_id,
        image_path=image_path,
        metadata_path=resolved_metadata_path,
        completion_path=completion_path,
        image_sha256=image_sha256,
        metadata_sha256=metadata_sha256,
        completion_sha256=hashlib.sha256(completion_bytes).hexdigest(),
        image_bytes=image_bytes,
        metadata=metadata,
        warnings=verified_warnings,
        oracle_eligible=_is_oracle_eligible(metadata, verified_warnings),
    )


def evidence_reference(evidence: EvidenceBundle) -> dict[str, object]:
    capture = evidence.metadata.get("capture")
    capture_conditions: dict[str, Any] = {}
    if isinstance(capture, dict):
        capture_conditions = {
            key: capture.get(key)
            for key in (
                "backend",
                "occlusionSensitive",
                "requiresVisibleDesktop",
                "stabilityCheckPerformed",
            )
        }
    return {
        "evidenceId": evidence.evidence_id,
        "image": {
            "path": str(evidence.image_path),
            "sha256": evidence.image_sha256,
        },
        "metadata": {
            "path": str(evidence.metadata_path),
            "sha256": evidence.metadata_sha256,
        },
        "completion": {
            "path": str(evidence.completion_path),
            "sha256": evidence.completion_sha256,
            "protocol": "evidence-commit-marker-v1",
        },
        "captureWarnings": list(evidence.warnings),
        "captureConditions": capture_conditions,
        "oracleEligible": evidence.oracle_eligible,
    }


def _read_bytes(path: Path) -> bytes:
    try:
        return path.read_bytes()
    except OSError as error:
        raise GameDriverError(
            "evidenceReadFailed",
            f"Evidence file could not be read: {path}: {error}",
            3,
        ) from error


def _parse_json(content: bytes, path: Path) -> dict[str, Any]:
    try:
        value = json.loads(content.decode("utf-8"))
    except (UnicodeError, json.JSONDecodeError) as error:
        raise _invalid_evidence(f"invalid JSON in {path}: {error}") from error
    if not isinstance(value, dict):
        raise _invalid_evidence(f"JSON root must be an object: {path}")
    return value


def _invalid_evidence(message: str) -> GameDriverError:
    return GameDriverError("evidenceIntegrityFailed", message, 3)


def _is_oracle_eligible(
    metadata: dict[str, Any],
    warnings: tuple[dict[str, str], ...],
) -> bool:
    capture = metadata.get("capture")
    window = metadata.get("window")
    window_after_capture = metadata.get("windowAfterCapture")
    coordinate_system = metadata.get("coordinateSystem")
    reference_space = (
        coordinate_system.get("referenceSpace")
        if isinstance(coordinate_system, dict)
        else None
    )
    return (
        metadata.get("evidenceRole") == "rawExternalObservation"
        and metadata.get("source") == "BetterBTD.GameDriver"
        and isinstance(capture, dict)
        and capture.get("backend") == "desktop-gdi-bitblt"
        and capture.get("occlusionSensitive") is True
        and capture.get("requiresVisibleDesktop") is True
        and isinstance(window, dict)
        and window.get("visible") is True
        and window.get("minimized") is False
        and window.get("foreground") is True
        and isinstance(window_after_capture, dict)
        and window_after_capture.get("visible") is True
        and window_after_capture.get("minimized") is False
        and window_after_capture.get("foreground") is True
        and isinstance(coordinate_system, dict)
        and coordinate_system.get("id") == "clientPhysicalPixels"
        and coordinate_system.get("hasReferenceAspectRatio") is True
        and isinstance(reference_space, dict)
        and reference_space.get("id") == "btd6Reference1920x1080"
        and reference_space.get("width") == 1920
        and reference_space.get("height") == 1080
        and not warnings
    )
