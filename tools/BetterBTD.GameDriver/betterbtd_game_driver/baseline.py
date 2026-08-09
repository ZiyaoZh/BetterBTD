from __future__ import annotations

import hashlib
import os
import uuid
from io import BytesIO
from pathlib import Path

from PIL import Image

from .errors import GameDriverError
from .evidence import EvidenceBundle, read_evidence
from .visual_catalog import VisualAnchor, VisualCatalog, VisualNumberGlyph


def build_templates(
    catalog: VisualCatalog,
    *,
    overwrite: bool,
) -> dict[str, object]:
    template_sources: list[
        tuple[
            VisualAnchor | VisualNumberGlyph,
            str,
            dict[str, object],
            int | None,
        ]
    ] = []
    for page in catalog.pages:
        for anchor in page.anchors:
            template_sources.append(
                (anchor, f"Anchor {anchor.id}", {"anchorId": anchor.id}, None)
            )
    for model in catalog.number_models:
        for glyph in model.glyphs:
            template_sources.append(
                (
                    glyph,
                    f"Number model {model.id} digit {glyph.digit}",
                    {"numberModelId": model.id, "digit": glyph.digit},
                    (
                        model.foreground_minimum
                        if model.uses_binary_alpha_mask
                        else None
                    ),
                )
            )

    generated: list[
        tuple[VisualAnchor | VisualNumberGlyph, bytes, dict[str, object]]
    ] = []
    evidence_cache: dict[Path, tuple[EvidenceBundle, Image.Image]] = {}
    try:
        for (
            template_source,
            source_label,
            descriptor,
            number_foreground_minimum,
        ) in template_sources:
            cached = evidence_cache.get(template_source.source_metadata_path)
            if cached is None:
                evidence = read_evidence(template_source.source_metadata_path)
                try:
                    with Image.open(BytesIO(evidence.image_bytes)) as source:
                        source.load()
                        image = source.convert("RGB")
                except (OSError, ValueError) as error:
                    raise GameDriverError(
                        "baselineSourceInvalid",
                        f"Baseline source image could not be read: {evidence.image_path}: {error}",
                        3,
                    ) from error
                if image.size != (catalog.reference_width, catalog.reference_height):
                    raise GameDriverError(
                        "baselineSourceSizeInvalid",
                        "Baseline source must use the exact catalog reference size; "
                        f"found {image.width} x {image.height}.",
                        3,
                    )
                cached = (evidence, image)
                evidence_cache[template_source.source_metadata_path] = cached
            evidence, image = cached
            if evidence.evidence_id != template_source.source_evidence_id:
                raise GameDriverError(
                    "baselineProvenanceMismatch",
                    f"{source_label} source evidenceId does not match its catalog entry.",
                    3,
                )
            if evidence.image_sha256 != template_source.source_image_sha256:
                raise GameDriverError(
                    "baselineProvenanceMismatch",
                    f"{source_label} source image hash does not match its catalog entry.",
                    3,
                )

            template = image.crop(
                (
                    template_source.source_bounds.x,
                    template_source.source_bounds.y,
                    template_source.source_bounds.right,
                    template_source.source_bounds.bottom,
                )
            )
            if number_foreground_minimum is not None:
                template = _number_glyph_mask(
                    template,
                    number_foreground_minimum,
                )
            content = _encode_png(template)
            actual_sha256 = hashlib.sha256(content).hexdigest()
            if actual_sha256 != template_source.template_sha256:
                raise GameDriverError(
                    "baselineTemplateMismatch",
                    f"{source_label} generated template hash is {actual_sha256}, "
                    f"but the catalog requires {template_source.template_sha256}.",
                    3,
                )
            generated.append(
                (
                    template_source,
                    content,
                    {
                        **descriptor,
                        "path": str(template_source.template_path),
                        "sha256": actual_sha256,
                        "sourceEvidenceId": template_source.source_evidence_id,
                    },
                )
            )
    finally:
        for _, image in evidence_cache.values():
            image.close()

    if not overwrite:
        existing_paths = [anchor.template_path for anchor, _, _ in generated if anchor.template_path.exists()]
        if existing_paths:
            formatted = ", ".join(str(path) for path in existing_paths)
            raise GameDriverError(
                "outputExists",
                f"Templates already exist: {formatted}. Use --overwrite to replace them.",
                5,
            )

    for anchor, content, _ in generated:
        _write_template(anchor.template_path, content, overwrite=overwrite)

    return {
        "schemaVersion": 1,
        "catalog": {"id": catalog.id, "version": catalog.version},
        "templates": [item for _, _, item in generated],
    }


def _encode_png(image: Image.Image) -> bytes:
    buffer = BytesIO()
    image.save(buffer, format="PNG", optimize=False, compress_level=9)
    return buffer.getvalue()


def _number_glyph_mask(
    image: Image.Image,
    foreground_minimum: int,
) -> Image.Image:
    rgb = image.convert("RGB")
    source_pixels = rgb.load()
    masked = Image.new("RGBA", rgb.size, (0, 0, 0, 0))
    masked_pixels = masked.load()
    for y in range(rgb.height):
        for x in range(rgb.width):
            red, green, blue = source_pixels[x, y]
            if red == green == blue and red >= foreground_minimum:
                masked_pixels[x, y] = (255, 255, 255, 255)
    return masked


def _write_template(path: Path, content: bytes, *, overwrite: bool) -> None:
    if path.exists() and not overwrite:
        raise GameDriverError(
            "outputExists",
            f"Template already exists: {path}. Use --overwrite to replace it.",
            5,
        )
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        temporary_path.write_bytes(content)
        if not overwrite and path.exists():
            raise GameDriverError("outputExists", f"Template already exists: {path}.", 5)
        os.replace(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)
