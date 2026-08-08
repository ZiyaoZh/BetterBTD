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
    source_bounds: Rect
    template_path: Path
    template_sha256: str
    minimum_score: float
    source_evidence_id: str
    source_image_sha256: str
    source_metadata_path: Path
    template_bytes: bytes | None
    page_anchor: bool = True


@dataclass(frozen=True, slots=True)
class VisualElementPlacement:
    view_state_id: str
    bounds: Rect
    action_point: tuple[int, int] | None
    anchor_ids: tuple[str, ...]
    states: tuple[VisualElementState, ...] = ()


@dataclass(frozen=True, slots=True)
class VisualElementState:
    id: str
    anchor_ids: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class VisualElement:
    id: str
    role: str
    bounds: Rect
    action_point: tuple[int, int] | None
    anchor_ids: tuple[str, ...]
    placements: tuple[VisualElementPlacement, ...] = ()


@dataclass(frozen=True, slots=True)
class VisualPositiveHoldout:
    evidence_id: str
    image_sha256: str
    metadata_path: Path


@dataclass(frozen=True, slots=True)
class VisualViewState:
    id: str
    minimum_score: float
    minimum_matched_anchors: int
    anchor_ids: tuple[str, ...]
    positive_holdout: VisualPositiveHoldout


@dataclass(frozen=True, slots=True)
class VisualPage:
    id: str
    kind: str
    minimum_score: float
    minimum_matched_anchors: int
    anchors: tuple[VisualAnchor, ...]
    elements: tuple[VisualElement, ...]
    positive_holdout: VisualPositiveHoldout
    view_states: tuple[VisualViewState, ...] = ()


@dataclass(frozen=True, slots=True)
class VisualCatalog:
    id: str
    version: int
    path: Path
    reference_width: int
    reference_height: int
    pages: tuple[VisualPage, ...]
    sha256: str
    schema_version: int = 1


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
    view_state_count = sum(len(page.view_states) for page in catalog.pages)
    placement_count = sum(
        len(element.placements)
        for page in catalog.pages
        for element in page.elements
    )
    return {
        "schemaVersion": 1,
        "catalog": {
            "id": catalog.id,
            "version": catalog.version,
            "schemaVersion": catalog.schema_version,
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
            "viewStateCount": view_state_count,
            "placementCount": placement_count,
        },
    }


def _parse_catalog(
    document: Any,
    path: Path,
    catalog_sha256: str,
    verify_templates: bool,
) -> VisualCatalog:
    root = _object(document, "catalog root")
    schema_version = _integer(root, "schemaVersion")
    if schema_version not in (1, 2):
        raise ValueError("schemaVersion must be 1 or 2")

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
        if kind not in ("page", "modal"):
            raise ValueError(f"page {page_id} kind must be page or modal")
        minimum_score = _score(page, "minimumScore")
        minimum_matched_anchors = _positive_integer(page, "minimumMatchedAnchors")

        anchors = _parse_anchors(
            page,
            path,
            reference_width,
            reference_height,
            page_id,
            schema_version,
            verify_templates,
            source_evidence_cache,
        )
        page_anchor_count = sum(anchor.page_anchor for anchor in anchors)
        if minimum_matched_anchors > page_anchor_count:
            raise ValueError(
                f"page {page_id} minimumMatchedAnchors exceeds its page anchor count"
            )

        positive_holdout = _parse_positive_holdout(
            page,
            path,
            page_id,
            {anchor.source_image_sha256 for anchor in anchors},
            source_evidence_cache,
        )

        view_states = _parse_view_states(
            page,
            path,
            page_id,
            schema_version,
            {anchor.id: anchor for anchor in anchors},
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
            {view_state.id for view_state in view_states},
            schema_version,
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
                view_states=tuple(view_states),
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
        for holdout in (
            page.positive_holdout,
            *(view_state.positive_holdout for view_state in page.view_states),
        ):
            holdout_path = holdout.metadata_path
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
        schema_version=schema_version,
    )


def _parse_anchors(
    page: dict[str, Any],
    catalog_path: Path,
    reference_width: int,
    reference_height: int,
    page_id: str,
    schema_version: int,
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
        raw_source_bounds = anchor.get("sourceBounds")
        if raw_source_bounds is not None and schema_version < 2:
            raise ValueError("anchor sourceBounds require catalog schemaVersion 2")
        source_bounds = (
            _rect(raw_source_bounds, f"anchor {anchor_id} sourceBounds")
            if raw_source_bounds is not None
            else bounds
        )
        _validate_rect(
            source_bounds,
            reference_width,
            reference_height,
            f"anchor {anchor_id} sourceBounds",
        )
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
                source_bounds=source_bounds,
                template_path=template_path,
                template_sha256=expected_sha256,
                minimum_score=_score(anchor, "minimumScore"),
                source_evidence_id=source_evidence_id,
                source_image_sha256=source_image_sha256,
                source_metadata_path=source_metadata_path,
                template_bytes=template_bytes,
                page_anchor=_optional_boolean(anchor, "pageAnchor", True),
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
    return _parse_positive_holdout_document(
        holdout,
        catalog_path,
        f"page {page_id}",
        source_image_hashes,
        evidence_cache,
    )


def _parse_positive_holdout_document(
    holdout: dict[str, Any],
    catalog_path: Path,
    context: str,
    source_image_hashes: set[str],
    evidence_cache: dict[Path, EvidenceBundle],
) -> VisualPositiveHoldout:
    catalog_root = catalog_path.parent.resolve()
    metadata_relative = Path(_nonempty_string(holdout, "evidence"))
    metadata_path = (catalog_root / metadata_relative).resolve()
    if not metadata_path.is_relative_to(catalog_root):
        raise ValueError(f"{context} positive holdout escapes the catalog directory")
    if metadata_path.suffix.casefold() != ".json" or metadata_path.name.endswith(
        ".complete.json"
    ):
        raise ValueError(f"{context} positive holdout must be capture metadata JSON")

    evidence = evidence_cache.get(metadata_path)
    if evidence is None:
        evidence = read_evidence(metadata_path)
        evidence_cache[metadata_path] = evidence
    evidence_id = _nonempty_string(holdout, "evidenceId")
    image_sha256 = _sha256(holdout, "imageSha256")
    if evidence.evidence_id != evidence_id:
        raise ValueError(f"{context} positive holdout evidenceId does not match")
    if evidence.image_sha256 != image_sha256:
        raise ValueError(f"{context} positive holdout image hash does not match")
    if not evidence.oracle_eligible:
        raise ValueError(f"{context} positive holdout is not Oracle eligible")
    if image_sha256 in source_image_hashes:
        raise ValueError(
            f"{context} positive holdout image must differ from every template source image"
        )
    return VisualPositiveHoldout(
        evidence_id=evidence_id,
        image_sha256=image_sha256,
        metadata_path=metadata_path,
    )


def _parse_view_states(
    page: dict[str, Any],
    catalog_path: Path,
    page_id: str,
    schema_version: int,
    anchors_by_id: dict[str, VisualAnchor],
    source_image_hashes: set[str],
    evidence_cache: dict[Path, EvidenceBundle],
) -> list[VisualViewState]:
    raw_view_states = page.get("viewStates", [])
    if not isinstance(raw_view_states, list):
        raise TypeError(f"page {page_id} viewStates must be an array")
    if raw_view_states and schema_version < 2:
        raise ValueError("viewStates require catalog schemaVersion 2")

    seen_ids: set[str] = set()
    view_states: list[VisualViewState] = []
    for index, raw_view_state in enumerate(raw_view_states):
        view_state = _object(
            raw_view_state,
            f"page {page_id} viewStates[{index}]",
        )
        view_state_id = _stable_id(view_state, "id")
        if not view_state_id.startswith(f"{page_id}."):
            raise ValueError(
                f"view state id {view_state_id} must start with {page_id}."
            )
        _require_unique(view_state_id, seen_ids, f"page {page_id} view state id")
        anchor_ids = _parse_anchor_ids(
            view_state,
            f"view state {view_state_id}",
            set(anchors_by_id),
            required=True,
        )
        page_anchor_ids = {
            anchor_id
            for anchor_id in anchor_ids
            if anchors_by_id[anchor_id].page_anchor
        }
        if page_anchor_ids:
            formatted = ", ".join(sorted(page_anchor_ids))
            raise ValueError(
                f"view state {view_state_id} must use detector-only anchors: {formatted}"
            )
        minimum_matched_anchors = _positive_integer(
            view_state,
            "minimumMatchedAnchors",
        )
        if minimum_matched_anchors > len(anchor_ids):
            raise ValueError(
                f"view state {view_state_id} minimumMatchedAnchors exceeds its anchor count"
            )
        holdout_document = _object(
            view_state.get("positiveHoldout"),
            f"view state {view_state_id} positiveHoldout",
        )
        positive_holdout = _parse_positive_holdout_document(
            holdout_document,
            catalog_path,
            f"view state {view_state_id}",
            source_image_hashes,
            evidence_cache,
        )
        view_states.append(
            VisualViewState(
                id=view_state_id,
                minimum_score=_score(view_state, "minimumScore"),
                minimum_matched_anchors=minimum_matched_anchors,
                anchor_ids=anchor_ids,
                positive_holdout=positive_holdout,
            )
        )
    return view_states


def _parse_elements(
    page: dict[str, Any],
    reference_width: int,
    reference_height: int,
    page_id: str,
    seen_ids: set[str],
    page_anchor_ids: set[str],
    page_view_state_ids: set[str],
    schema_version: int,
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
        raw_placements = element.get("placements")
        placements: list[VisualElementPlacement] = []
        if raw_placements is not None:
            if schema_version < 2:
                raise ValueError("element placements require catalog schemaVersion 2")
            if any(
                name in element for name in ("bounds", "actionPoint", "anchorIds")
            ):
                raise ValueError(
                    f"element {element_id} cannot mix placements with legacy geometry"
                )
            if not isinstance(raw_placements, list) or not raw_placements:
                raise ValueError(f"element {element_id} placements must be a non-empty array")
            seen_view_state_ids: set[str] = set()
            for placement_index, raw_placement in enumerate(raw_placements):
                placement = _object(
                    raw_placement,
                    f"element {element_id} placements[{placement_index}]",
                )
                view_state_id = _stable_id(placement, "viewStateId")
                if view_state_id not in page_view_state_ids:
                    raise ValueError(
                        f"element {element_id} placement references unknown view state: "
                        f"{view_state_id}"
                    )
                _require_unique(
                    view_state_id,
                    seen_view_state_ids,
                    f"element {element_id} placement viewStateId",
                )
                placement_bounds = _rect(
                    placement.get("bounds"),
                    f"element {element_id} placement bounds",
                )
                _validate_rect(
                    placement_bounds,
                    reference_width,
                    reference_height,
                    f"element {element_id} placement",
                )
                placements.append(
                    VisualElementPlacement(
                        view_state_id=view_state_id,
                        bounds=placement_bounds,
                        action_point=_parse_action_point(
                            placement,
                            placement_bounds,
                            f"element {element_id} placement",
                        ),
                        anchor_ids=_parse_anchor_ids(
                            placement,
                            f"element {element_id} placement",
                            page_anchor_ids,
                            required=True,
                        ),
                        states=_parse_element_states(
                            placement,
                            f"element {element_id} placement",
                            page_anchor_ids,
                        ),
                    )
                )
            bounds = placements[0].bounds
            action_point = None
            anchor_ids: tuple[str, ...] = ()
        else:
            bounds = _rect(element.get("bounds"), f"element {element_id} bounds")
            _validate_rect(
                bounds,
                reference_width,
                reference_height,
                f"element {element_id}",
            )
            action_point = _parse_action_point(element, bounds, f"element {element_id}")
            anchor_ids = _parse_anchor_ids(
                element,
                f"element {element_id}",
                page_anchor_ids,
                required=False,
            )
        elements.append(
            VisualElement(
                id=element_id,
                role=role,
                bounds=bounds,
                action_point=action_point,
                anchor_ids=anchor_ids,
                placements=tuple(placements),
            )
        )
    return elements


def _parse_element_states(
    placement: dict[str, Any],
    context: str,
    valid_anchor_ids: set[str],
) -> tuple[VisualElementState, ...]:
    raw_states = placement.get("states", [])
    if not isinstance(raw_states, list):
        raise TypeError(f"{context} states must be an array")
    seen_ids: set[str] = set()
    states: list[VisualElementState] = []
    for index, raw_state in enumerate(raw_states):
        state = _object(raw_state, f"{context} states[{index}]")
        state_id = _stable_id(state, "id")
        _require_unique(state_id, seen_ids, f"{context} state id")
        states.append(
            VisualElementState(
                id=state_id,
                anchor_ids=_parse_anchor_ids(
                    state,
                    f"{context} state {state_id}",
                    valid_anchor_ids,
                    required=True,
                ),
            )
        )
    return tuple(states)


def _parse_action_point(
    value: dict[str, Any],
    bounds: Rect,
    context: str,
) -> tuple[int, int] | None:
    raw_action_point = value.get("actionPoint")
    if raw_action_point is None:
        return None
    action = _object(raw_action_point, f"{context} actionPoint")
    x = _integer(action, "x")
    y = _integer(action, "y")
    if x < bounds.x or x >= bounds.right or y < bounds.y or y >= bounds.bottom:
        raise ValueError(f"{context} actionPoint must be inside bounds")
    return x, y


def _parse_anchor_ids(
    value: dict[str, Any],
    context: str,
    valid_anchor_ids: set[str],
    *,
    required: bool,
) -> tuple[str, ...]:
    raw_anchor_ids = value.get("anchorIds", [])
    if not isinstance(raw_anchor_ids, list) or not all(
        isinstance(item, str) for item in raw_anchor_ids
    ):
        raise TypeError(f"{context} anchorIds must be an array of strings")
    anchor_ids = tuple(raw_anchor_ids)
    if required and not anchor_ids:
        raise ValueError(f"{context} anchorIds must not be empty")
    if len(set(anchor_ids)) != len(anchor_ids):
        raise ValueError(f"{context} anchorIds must not contain duplicates")
    unknown_anchor_ids = set(anchor_ids) - valid_anchor_ids
    if unknown_anchor_ids:
        formatted = ", ".join(sorted(unknown_anchor_ids))
        raise ValueError(f"{context} references unknown anchors: {formatted}")
    return anchor_ids


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


def _optional_boolean(value: dict[str, Any], name: str, default: bool) -> bool:
    result = value.get(name, default)
    if not isinstance(result, bool):
        raise TypeError(f"{name} must be a boolean")
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
