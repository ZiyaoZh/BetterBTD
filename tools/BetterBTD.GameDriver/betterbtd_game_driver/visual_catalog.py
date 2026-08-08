from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .evidence import EvidenceBundle, read_evidence
from .errors import GameDriverError
from .models import Rect


DEFAULT_CATALOG_PATH = (
    Path(__file__).resolve().parent.parent / "visual-baselines" / "catalog.json"
)


@dataclass(frozen=True, slots=True)
class VisualAnchor:
    id: str
    bounds: Rect
    template_path: Path
    template_sha256: str
    minimum_score: float
    source_evidence_id: str
    source_image_sha256: str
    source_metadata_path: Path
    template_bytes: bytes | None


@dataclass(frozen=True, slots=True)
class VisualElement:
    id: str
    role: str
    bounds: Rect
    action_point: tuple[int, int] | None
    anchor_ids: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class VisualPositiveHoldout:
    evidence_id: str
    image_sha256: str
    metadata_path: Path


@dataclass(frozen=True, slots=True)
class VisualPage:
    id: str
    kind: str
    minimum_score: float
    minimum_matched_anchors: int
    anchors: tuple[VisualAnchor, ...]
    elements: tuple[VisualElement, ...]
    positive_holdout: VisualPositiveHoldout


@dataclass(frozen=True, slots=True)
class VisualCatalog:
    id: str
    version: int
    path: Path
    reference_width: int
    reference_height: int
    pages: tuple[VisualPage, ...]
    sha256: str


def load_visual_catalog(
    path: Path | None = None,
    *,
    verify_templates: bool = True,
) -> VisualCatalog:
    catalog_path = (path or DEFAULT_CATALOG_PATH).expanduser().resolve()
    try:
        catalog_bytes = catalog_path.read_bytes()
        document = json.loads(catalog_bytes.decode("utf-8"))
    except FileNotFoundError as error:
        raise GameDriverError(
            "visualCatalogNotFound",
            f"Visual catalog does not exist: {catalog_path}",
            3,
        ) from error
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GameDriverError(
            "visualCatalogInvalid",
            f"Visual catalog could not be read: {catalog_path}: {error}",
            3,
        ) from error

    try:
        return _parse_catalog(
            document,
            catalog_path,
            hashlib.sha256(catalog_bytes).hexdigest(),
            verify_templates,
        )
    except (KeyError, TypeError, ValueError) as error:
        raise GameDriverError(
            "visualCatalogInvalid",
            f"Visual catalog is invalid: {catalog_path}: {error}",
            3,
        ) from error


def visual_catalog_summary(catalog: VisualCatalog) -> dict[str, object]:
    template_count = sum(len(page.anchors) for page in catalog.pages)
    element_count = sum(len(page.elements) for page in catalog.pages)
    return {
        "schemaVersion": 1,
        "catalog": {
            "id": catalog.id,
            "version": catalog.version,
            "path": str(catalog.path),
            "sha256": catalog.sha256,
        },
        "referenceSpace": {
            "id": "btd6Reference1920x1080",
            "width": catalog.reference_width,
            "height": catalog.reference_height,
        },
        "validation": {
            "valid": True,
            "pageCount": len(catalog.pages),
            "templateCount": template_count,
            "elementCount": element_count,
        },
    }


def _parse_catalog(
    document: Any,
    path: Path,
    catalog_sha256: str,
    verify_templates: bool,
) -> VisualCatalog:
    root = _object(document, "catalog root")
    if _integer(root, "schemaVersion") != 1:
        raise ValueError("schemaVersion must be 1")

    catalog_id = _nonempty_string(root, "catalogId")
    catalog_version = _positive_integer(root, "catalogVersion")
    reference = _object(root.get("referenceSpace"), "referenceSpace")
    if _nonempty_string(reference, "id") != "btd6Reference1920x1080":
        raise ValueError("referenceSpace.id must be btd6Reference1920x1080")
    reference_width = _positive_integer(reference, "width")
    reference_height = _positive_integer(reference, "height")
    if (reference_width, reference_height) != (1920, 1080):
        raise ValueError("referenceSpace must be 1920 x 1080")

    raw_pages = _array(root, "pages")
    if not raw_pages:
        raise ValueError("pages must not be empty")

    seen_page_ids: set[str] = set()
    seen_element_ids: set[str] = set()
    source_evidence_cache: dict[Path, EvidenceBundle] = {}
    pages: list[VisualPage] = []
    for index, raw_page in enumerate(raw_pages):
        page = _object(raw_page, f"pages[{index}]")
        page_id = _stable_id(page, "id")
        _require_unique(page_id, seen_page_ids, "page id")
        kind = _stable_id(page, "kind")
        if kind != "page":
            raise ValueError(f"page {page_id} kind must currently be page")
        minimum_score = _score(page, "minimumScore")
        minimum_matched_anchors = _positive_integer(page, "minimumMatchedAnchors")

        anchors = _parse_anchors(
            page,
            path,
            reference_width,
            reference_height,
            page_id,
            verify_templates,
            source_evidence_cache,
        )
        if minimum_matched_anchors > len(anchors):
            raise ValueError(
                f"page {page_id} minimumMatchedAnchors exceeds its anchor count"
            )

        positive_holdout = _parse_positive_holdout(
            page,
            path,
            page_id,
            {anchor.source_image_sha256 for anchor in anchors},
            source_evidence_cache,
        )

        elements = _parse_elements(
            page,
            reference_width,
            reference_height,
            page_id,
            seen_element_ids,
            {anchor.id for anchor in anchors},
        )
        pages.append(
            VisualPage(
                id=page_id,
                kind=kind,
                minimum_score=minimum_score,
                minimum_matched_anchors=minimum_matched_anchors,
                anchors=tuple(anchors),
                elements=tuple(elements),
                positive_holdout=positive_holdout,
            )
        )

    all_anchors = [anchor for page in pages for anchor in page.anchors]
    template_paths = [anchor.template_path for anchor in all_anchors]
    if len(set(template_paths)) != len(template_paths):
        raise ValueError("template paths must be unique across the catalog")
    protected_source_paths: set[Path] = set()
    for anchor in all_anchors:
        metadata_path = anchor.source_metadata_path
        protected_source_paths.update(
            (
                metadata_path,
                metadata_path.with_suffix(".png"),
                metadata_path.with_name(f"{metadata_path.stem}.complete.json"),
            )
        )
    for page in pages:
        holdout_path = page.positive_holdout.metadata_path
        protected_source_paths.update(
            (
                holdout_path,
                holdout_path.with_suffix(".png"),
                holdout_path.with_name(f"{holdout_path.stem}.complete.json"),
            )
        )
    collisions = set(template_paths) & protected_source_paths
    if collisions:
        formatted = ", ".join(str(path) for path in sorted(collisions))
        raise ValueError(f"template paths overlap protected source evidence: {formatted}")

    return VisualCatalog(
        id=catalog_id,
        version=catalog_version,
        path=path,
        reference_width=reference_width,
        reference_height=reference_height,
        pages=tuple(pages),
        sha256=catalog_sha256,
    )


def _parse_anchors(
    page: dict[str, Any],
    catalog_path: Path,
    reference_width: int,
    reference_height: int,
    page_id: str,
    verify_templates: bool,
    source_evidence_cache: dict[Path, EvidenceBundle],
) -> list[VisualAnchor]:
    raw_anchors = _array(page, "anchors")
    if not raw_anchors:
        raise ValueError(f"page {page_id} must contain at least one anchor")

    seen_ids: set[str] = set()
    anchors: list[VisualAnchor] = []
    catalog_root = catalog_path.parent.resolve()
    for index, raw_anchor in enumerate(raw_anchors):
        anchor = _object(raw_anchor, f"page {page_id} anchors[{index}]")
        anchor_id = _stable_id(anchor, "id")
        _require_unique(anchor_id, seen_ids, f"page {page_id} anchor id")
        bounds = _rect(anchor.get("bounds"), f"anchor {anchor_id} bounds")
        _validate_rect(bounds, reference_width, reference_height, f"anchor {anchor_id}")
        template_relative = Path(_nonempty_string(anchor, "template"))
        template_path = (catalog_root / template_relative).resolve()
        if not template_path.is_relative_to(catalog_root):
            raise ValueError(f"anchor {anchor_id} template escapes the catalog directory")
        if template_path.suffix.casefold() != ".png":
            raise ValueError(f"anchor {anchor_id} template must be a PNG file")
        expected_sha256 = _sha256(anchor, "templateSha256")
        template_bytes: bytes | None = None
        if verify_templates:
            if not template_path.is_file():
                raise ValueError(f"anchor {anchor_id} template does not exist: {template_path}")
            template_bytes = template_path.read_bytes()
            actual_sha256 = hashlib.sha256(template_bytes).hexdigest()
            if actual_sha256 != expected_sha256:
                raise ValueError(
                    f"anchor {anchor_id} template hash mismatch: expected "
                    f"{expected_sha256}, found {actual_sha256}"
                )
        source_metadata_relative = Path(_nonempty_string(anchor, "sourceEvidence"))
        source_metadata_path = (catalog_root / source_metadata_relative).resolve()
        if not source_metadata_path.is_relative_to(catalog_root):
            raise ValueError(f"anchor {anchor_id} source evidence escapes the catalog directory")
        source_evidence = source_evidence_cache.get(source_metadata_path)
        if source_evidence is None:
            source_evidence = read_evidence(source_metadata_path)
            source_evidence_cache[source_metadata_path] = source_evidence
        source_evidence_id = _nonempty_string(anchor, "sourceEvidenceId")
        source_image_sha256 = _sha256(anchor, "sourceImageSha256")
        if source_evidence.evidence_id != source_evidence_id:
            raise ValueError(f"anchor {anchor_id} source evidenceId does not match")
        if source_evidence.image_sha256 != source_image_sha256:
            raise ValueError(f"anchor {anchor_id} source image hash does not match")
        if not source_evidence.oracle_eligible:
            raise ValueError(f"anchor {anchor_id} source evidence is not Oracle eligible")
        anchors.append(
            VisualAnchor(
                id=anchor_id,
                bounds=bounds,
                template_path=template_path,
                template_sha256=expected_sha256,
                minimum_score=_score(anchor, "minimumScore"),
                source_evidence_id=source_evidence_id,
                source_image_sha256=source_image_sha256,
                source_metadata_path=source_metadata_path,
                template_bytes=template_bytes,
            )
        )
    return anchors


def _parse_positive_holdout(
    page: dict[str, Any],
    catalog_path: Path,
    page_id: str,
    source_image_hashes: set[str],
    evidence_cache: dict[Path, EvidenceBundle],
) -> VisualPositiveHoldout:
    holdout = _object(page.get("positiveHoldout"), f"page {page_id} positiveHoldout")
    catalog_root = catalog_path.parent.resolve()
    metadata_relative = Path(_nonempty_string(holdout, "evidence"))
    metadata_path = (catalog_root / metadata_relative).resolve()
    if not metadata_path.is_relative_to(catalog_root):
        raise ValueError(f"page {page_id} positive holdout escapes the catalog directory")
    if metadata_path.suffix.casefold() != ".json" or metadata_path.name.endswith(
        ".complete.json"
    ):
        raise ValueError(f"page {page_id} positive holdout must be capture metadata JSON")

    evidence = evidence_cache.get(metadata_path)
    if evidence is None:
        evidence = read_evidence(metadata_path)
        evidence_cache[metadata_path] = evidence
    evidence_id = _nonempty_string(holdout, "evidenceId")
    image_sha256 = _sha256(holdout, "imageSha256")
    if evidence.evidence_id != evidence_id:
        raise ValueError(f"page {page_id} positive holdout evidenceId does not match")
    if evidence.image_sha256 != image_sha256:
        raise ValueError(f"page {page_id} positive holdout image hash does not match")
    if not evidence.oracle_eligible:
        raise ValueError(f"page {page_id} positive holdout is not Oracle eligible")
    if image_sha256 in source_image_hashes:
        raise ValueError(
            f"page {page_id} positive holdout image must differ from every template source image"
        )
    return VisualPositiveHoldout(
        evidence_id=evidence_id,
        image_sha256=image_sha256,
        metadata_path=metadata_path,
    )


def _parse_elements(
    page: dict[str, Any],
    reference_width: int,
    reference_height: int,
    page_id: str,
    seen_ids: set[str],
    page_anchor_ids: set[str],
) -> list[VisualElement]:
    raw_elements = _array(page, "elements")
    elements: list[VisualElement] = []
    for index, raw_element in enumerate(raw_elements):
        element = _object(raw_element, f"page {page_id} elements[{index}]")
        element_id = _stable_id(element, "id")
        if not element_id.startswith(f"{page_id}."):
            raise ValueError(f"element id {element_id} must start with {page_id}.")
        _require_unique(element_id, seen_ids, "element id")
        role = _stable_id(element, "role")
        bounds = _rect(element.get("bounds"), f"element {element_id} bounds")
        _validate_rect(bounds, reference_width, reference_height, f"element {element_id}")

        raw_action_point = element.get("actionPoint")
        action_point: tuple[int, int] | None = None
        if raw_action_point is not None:
            action = _object(raw_action_point, f"element {element_id} actionPoint")
            x = _integer(action, "x")
            y = _integer(action, "y")
            if x < bounds.x or x >= bounds.right or y < bounds.y or y >= bounds.bottom:
                raise ValueError(f"element {element_id} actionPoint must be inside bounds")
            action_point = (x, y)
        raw_anchor_ids = element.get("anchorIds", [])
        if not isinstance(raw_anchor_ids, list) or not all(
            isinstance(item, str) for item in raw_anchor_ids
        ):
            raise TypeError(f"element {element_id} anchorIds must be an array of strings")
        anchor_ids = tuple(raw_anchor_ids)
        if len(set(anchor_ids)) != len(anchor_ids):
            raise ValueError(f"element {element_id} anchorIds must not contain duplicates")
        unknown_anchor_ids = set(anchor_ids) - page_anchor_ids
        if unknown_anchor_ids:
            formatted = ", ".join(sorted(unknown_anchor_ids))
            raise ValueError(f"element {element_id} references unknown anchors: {formatted}")
        elements.append(
            VisualElement(
                id=element_id,
                role=role,
                bounds=bounds,
                action_point=action_point,
                anchor_ids=anchor_ids,
            )
        )
    return elements


def _object(value: Any, name: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise TypeError(f"{name} must be an object")
    return value


def _array(value: dict[str, Any], name: str) -> list[Any]:
    result = value.get(name)
    if not isinstance(result, list):
        raise TypeError(f"{name} must be an array")
    return result


def _nonempty_string(value: dict[str, Any], name: str) -> str:
    result = value.get(name)
    if not isinstance(result, str) or not result.strip():
        raise TypeError(f"{name} must be a non-empty string")
    return result


def _stable_id(value: dict[str, Any], name: str) -> str:
    result = _nonempty_string(value, name)
    if not all(character.isalnum() or character in ".-_" for character in result):
        raise ValueError(f"{name} contains unsupported characters: {result}")
    return result


def _integer(value: dict[str, Any], name: str) -> int:
    result = value.get(name)
    if not isinstance(result, int) or isinstance(result, bool):
        raise TypeError(f"{name} must be an integer")
    return result


def _positive_integer(value: dict[str, Any], name: str) -> int:
    result = _integer(value, name)
    if result <= 0:
        raise ValueError(f"{name} must be positive")
    return result


def _score(value: dict[str, Any], name: str) -> float:
    result = value.get(name)
    if not isinstance(result, (int, float)) or isinstance(result, bool):
        raise TypeError(f"{name} must be a number")
    result = float(result)
    if result < 0 or result > 1:
        raise ValueError(f"{name} must be between 0 and 1")
    return result


def _sha256(value: dict[str, Any], name: str) -> str:
    result = _nonempty_string(value, name).lower()
    if len(result) != 64 or any(character not in "0123456789abcdef" for character in result):
        raise ValueError(f"{name} must be a lowercase SHA-256 value")
    return result


def _rect(value: Any, name: str) -> Rect:
    document = _object(value, name)
    return Rect(
        x=_integer(document, "x"),
        y=_integer(document, "y"),
        width=_positive_integer(document, "width"),
        height=_positive_integer(document, "height"),
    )


def _validate_rect(rect: Rect, width: int, height: int, name: str) -> None:
    if rect.x < 0 or rect.y < 0 or rect.right > width or rect.bottom > height:
        raise ValueError(f"{name} bounds must be inside the reference space")


def _require_unique(value: str, seen: set[str], name: str) -> None:
    if value in seen:
        raise ValueError(f"duplicate {name}: {value}")
    seen.add(value)
