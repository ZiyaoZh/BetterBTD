from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path

from PIL import Image


def write_test_evidence(
    root: Path,
    name: str,
    image: Image.Image,
    *,
    warnings: list[dict[str, str]] | None = None,
    has_reference_aspect_ratio: bool = True,
) -> Path:
    image_path = root / f"{name}.png"
    metadata_path = root / f"{name}.json"
    completion_path = root / f"{name}.complete.json"
    image.save(image_path, format="PNG")
    image_sha256 = hashlib.sha256(image_path.read_bytes()).hexdigest()
    evidence_id = f"test-{name}"
    metadata = {
        "schemaVersion": 1,
        "evidenceId": evidence_id,
        "evidenceRole": "rawExternalObservation",
        "source": "BetterBTD.GameDriver",
        "capture": {
            "backend": "desktop-gdi-bitblt",
            "occlusionSensitive": True,
            "requiresVisibleDesktop": True,
            "stabilityCheckPerformed": False,
        },
        "window": {
            "visible": True,
            "minimized": False,
            "foreground": True,
            "clientRectOnScreen": {
                "x": 0,
                "y": 0,
                "width": image.width,
                "height": image.height,
            },
        },
        "windowAfterCapture": {
            "visible": True,
            "minimized": False,
            "foreground": True,
        },
        "coordinateSystem": {
            "id": "clientPhysicalPixels",
            "hasReferenceAspectRatio": has_reference_aspect_ratio,
            "bounds": {
                "xMinimum": 0,
                "yMinimum": 0,
                "xMaximumExclusive": image.width,
                "yMaximumExclusive": image.height,
            },
            "referenceSpace": {
                "id": "btd6Reference1920x1080",
                "width": 1920,
                "height": 1080,
            },
        },
        "files": {"image": {"format": "png-rgb8", "sha256": image_sha256}},
        "warnings": warnings or [],
    }
    metadata_bytes = (json.dumps(metadata, indent=2) + os.linesep).encode("utf-8")
    metadata_path.write_bytes(metadata_bytes)
    completion = {
        "schemaVersion": 1,
        "protocol": "evidence-commit-marker-v1",
        "evidenceId": evidence_id,
        "imageSha256": image_sha256,
        "metadataSha256": hashlib.sha256(metadata_bytes).hexdigest(),
    }
    completion_path.write_text(
        json.dumps(completion, indent=2) + os.linesep,
        encoding="utf-8",
    )
    return metadata_path
