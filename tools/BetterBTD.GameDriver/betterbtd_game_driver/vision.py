from __future__ import annotations

import os
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageOps, ImageStat

from .coordinates import reference_rect_to_client, reference_to_client
from .evidence import EvidenceBundle, evidence_reference
from .errors import GameDriverError
from .visual_catalog import VisualCatalog, VisualElement, VisualPage, VisualViewState


COMPARISON_SIZE = (48, 48)
PAGE_KIND_RANK = {"page": 0, "modal": 1}


@dataclass(frozen=True, slots=True)
class AnchorMatch:
    id: str
    score: float
    minimum_score: float
    matched: bool
    page_anchor: bool


@dataclass(frozen=True, slots=True)
class PageMatch:
    page: VisualPage
    score: float
    ranking_score: float
    matched_anchor_count: int
    matched: bool
    anchors: tuple[AnchorMatch, ...]
    view_state: ViewStateMatch | None = None
    view_state_ambiguous: bool = False
    view_state_candidates: tuple[ViewStateMatch, ...] = ()


@dataclass(frozen=True, slots=True)
class ViewStateMatch:
    view_state: VisualViewState
    score: float
    matched_anchor_count: int
    matched: bool


@dataclass(frozen=True, slots=True)
class FrameRecognition:
    status: str
    match: PageMatch | None
    candidates: tuple[PageMatch, ...]


def recognize_image(
    evidence: EvidenceBundle,
    catalog: VisualCatalog,
) -> tuple[dict[str, object], PageMatch | None]:
    resolved_image_path = evidence.image_path
    try:
        with Image.open(BytesIO(evidence.image_bytes)) as source:
            source.load()
            image_format = source.format
            image = ImageOps.exif_transpose(source).convert("RGB")
    except FileNotFoundError as error:
        raise GameDriverError(
            "observationImageNotFound",
            f"Observation image does not exist: {resolved_image_path}",
            3,
        ) from error
    except (OSError, ValueError) as error:
        raise GameDriverError(
            "observationImageInvalid",
            f"Observation image could not be read: {resolved_image_path}: {error}",
            3,
        ) from error

    width, height = image.size
    if image_format != "PNG":
        raise GameDriverError(
            "observationImageInvalid",
            f"Observation image must be PNG; found {image_format or 'unknown'}.",
            3,
        )
    _validate_evidence_geometry(evidence, width, height)
    frame_recognition = recognize_frame(image, catalog)
    best_match = frame_recognition.match

    observed_at = datetime.now(timezone.utc)
    result: dict[str, object] = {
        "schemaVersion": 1,
        "observationRole": "independentVisualInterpretation",
        "source": "BetterBTD.GameDriver",
        "observedAtUtc": observed_at.isoformat(timespec="milliseconds").replace(
            "+00:00", "Z"
        ),
        "evidence": evidence_reference(evidence),
        "image": {"format": image_format, "width": width, "height": height},
        "catalog": {
            "id": catalog.id,
            "version": catalog.version,
            "path": str(catalog.path),
            "sha256": catalog.sha256,
        },
        "coordinateSystem": {
            "input": "clientPhysicalPixels",
            "reference": "btd6Reference1920x1080",
            "rectangleConvention": "halfOpen",
        },
        "recognition": {
            "status": frame_recognition.status,
            "oracleEligible": best_match is not None and evidence.oracle_eligible,
            "page": (
                _page_result(best_match, width, height, evidence.oracle_eligible)
                if best_match
                else None
            ),
            "candidates": [
                _page_candidate(match)
                for match in frame_recognition.candidates
            ],
            "elements": _element_results(best_match, width, height) if best_match else [],
        },
    }
    return result, best_match


def recognize_frame(image: Image.Image, catalog: VisualCatalog) -> FrameRecognition:
    width, height = image.size
    expected_ratio = catalog.reference_width / catalog.reference_height
    actual_ratio = width / height
    if abs(expected_ratio - actual_ratio) > 0.01:
        raise GameDriverError(
            "unsupportedObservationAspectRatio",
            f"Observation image must be 16:9; found {width} x {height}.",
            3,
        )

    reference_image = image.convert("RGB").resize(
        (catalog.reference_width, catalog.reference_height),
        Image.Resampling.LANCZOS,
    )
    page_matches = tuple(_match_page(reference_image, page) for page in catalog.pages)
    ranked_pages = tuple(
        sorted(
            page_matches,
            key=lambda match: match.ranking_score,
            reverse=True,
        )
    )
    matched_pages = [match for match in ranked_pages if match.matched]
    best_match = max(
        matched_pages,
        key=lambda match: (
            PAGE_KIND_RANK[match.page.kind],
            match.ranking_score,
        ),
        default=None,
    )
    ambiguous = (
        best_match is not None
        and any(
            candidate is not best_match
            and candidate.page.kind == best_match.page.kind
            and abs(best_match.ranking_score - candidate.ranking_score) < 0.02
            and abs(best_match.score - candidate.score) < 0.02
            for candidate in ranked_pages
        )
    )
    if ambiguous:
        best_match = None

    return FrameRecognition(
        status="ambiguous" if ambiguous else ("matched" if best_match else "unknown"),
        match=best_match,
        candidates=ranked_pages,
    )


def write_annotation(
    image_path: Path,
    output_path: Path,
    page_match: PageMatch | None,
    *,
    overwrite: bool,
) -> None:
    resolved_input = image_path.expanduser().resolve()
    resolved_output = output_path.expanduser().resolve()
    if resolved_output.suffix.casefold() != ".png":
        raise GameDriverError(
            "invalidAnnotationOutput",
            "--annotated-output must name a .png file.",
            2,
        )
    if resolved_output.exists() and not overwrite:
        raise GameDriverError(
            "outputExists",
            f"Annotation output already exists: {resolved_output}. Use --overwrite to replace it.",
            5,
        )
    if resolved_output == resolved_input:
        raise GameDriverError(
            "protectedEvidenceOutput",
            "Annotated output cannot replace the source evidence image.",
            2,
        )

    try:
        with Image.open(resolved_input) as source:
            image = ImageOps.exif_transpose(source).convert("RGB")
    except (OSError, ValueError) as error:
        raise GameDriverError(
            "observationImageInvalid",
            f"Observation image could not be read: {resolved_input}: {error}",
            3,
        ) from error

    draw = ImageDraw.Draw(image)
    width, height = image.size
    label = "UNKNOWN"
    color = (220, 60, 60)
    if page_match is not None:
        label = f"{page_match.page.id} {page_match.score:.3f}"
        color = (30, 210, 100)
        anchor_matches = {anchor.id: anchor for anchor in page_match.anchors}
        for element in page_match.page.elements:
            placement = _element_placement_for_match(page_match, element)
            if placement is None:
                continue
            rect = reference_rect_to_client(
                placement.bounds,
                width,
                height,
            )
            detector_matches = [
                anchor_matches[anchor_id] for anchor_id in placement.anchor_ids
            ]
            element_color = (
                (30, 210, 100)
                if detector_matches and all(item.matched for item in detector_matches)
                else ((220, 60, 60) if detector_matches else (245, 180, 35))
            )
            draw.rectangle(
                (rect.x, rect.y, rect.right - 1, rect.bottom - 1),
                outline=element_color,
                width=max(2, round(width / 960)),
            )
            draw.text(
                (rect.x + 4, rect.y + 3),
                element.id,
                fill=(255, 255, 255),
                stroke_width=2,
                stroke_fill=(0, 0, 0),
                font=ImageFont.load_default(),
            )

    draw.rectangle((8, 8, 300, 39), fill=(0, 0, 0))
    draw.text((16, 15), label, fill=color, font=ImageFont.load_default())
    resolved_output.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = resolved_output.with_name(
        f"{resolved_output.name}.{uuid.uuid4().hex}.tmp"
    )
    try:
        image.save(temporary_path, format="PNG")
        if not overwrite and resolved_output.exists():
            raise GameDriverError(
                "outputExists",
                f"Annotation output already exists: {resolved_output}.",
                5,
            )
        os.replace(temporary_path, resolved_output)
    finally:
        temporary_path.unlink(missing_ok=True)


def _match_page(reference_image: Image.Image, page: VisualPage) -> PageMatch:
    anchor_matches: list[AnchorMatch] = []
    for anchor in page.anchors:
        target = reference_image.crop(
            (
                anchor.bounds.x,
                anchor.bounds.y,
                anchor.bounds.right,
                anchor.bounds.bottom,
            )
        )
        if anchor.template_bytes is None:
            raise GameDriverError(
                "visualCatalogInvalid",
                f"Anchor template was not loaded for recognition: {anchor.id}",
                3,
            )
        with Image.open(BytesIO(anchor.template_bytes)) as source_template:
            template = source_template.convert("RGB")
        score = _image_similarity(target, template)
        anchor_matches.append(
            AnchorMatch(
                id=anchor.id,
                score=score,
                minimum_score=anchor.minimum_score,
                matched=score >= anchor.minimum_score,
                page_anchor=anchor.page_anchor,
            )
        )

    page_anchor_matches = [match for match in anchor_matches if match.page_anchor]
    matched_anchor_count = sum(match.matched for match in page_anchor_matches)
    page_score = sum(match.score for match in page_anchor_matches) / len(
        page_anchor_matches
    )
    view_state, view_state_ambiguous, view_state_candidates = _match_view_states(
        page,
        anchor_matches,
    )
    page_requirements_matched = (
        matched_anchor_count >= page.minimum_matched_anchors
        and page_score >= page.minimum_score
    )
    score = page_score
    ranking_score = (
        round(
            (page_score + (view_state.score if view_state is not None else 0.0)) / 2,
            6,
        )
        if page.view_states
        else page_score
    )
    return PageMatch(
        page=page,
        score=score,
        ranking_score=ranking_score,
        matched_anchor_count=matched_anchor_count,
        matched=page_requirements_matched,
        anchors=tuple(anchor_matches),
        view_state=view_state,
        view_state_ambiguous=view_state_ambiguous,
        view_state_candidates=view_state_candidates,
    )


def _match_view_states(
    page: VisualPage,
    anchor_matches: list[AnchorMatch],
) -> tuple[ViewStateMatch | None, bool, tuple[ViewStateMatch, ...]]:
    if not page.view_states:
        return None, False, ()
    matches_by_id = {match.id: match for match in anchor_matches}
    candidates: list[ViewStateMatch] = []
    for view_state in page.view_states:
        state_anchors = [matches_by_id[anchor_id] for anchor_id in view_state.anchor_ids]
        score = sum(anchor.score for anchor in state_anchors) / len(state_anchors)
        matched_anchor_count = sum(anchor.matched for anchor in state_anchors)
        candidates.append(
            ViewStateMatch(
                view_state=view_state,
                score=score,
                matched_anchor_count=matched_anchor_count,
                matched=(
                    matched_anchor_count >= view_state.minimum_matched_anchors
                    and score >= view_state.minimum_score
                ),
            )
        )
    ranked = tuple(sorted(candidates, key=lambda candidate: candidate.score, reverse=True))
    best = ranked[0] if ranked and ranked[0].matched else None
    ambiguous = (
        best is not None
        and len(ranked) > 1
        and abs(ranked[0].score - ranked[1].score) < 0.02
    )
    return (None if ambiguous else best), ambiguous, ranked


def _image_similarity(first: Image.Image, second: Image.Image) -> float:
    first_normalized = first.resize(COMPARISON_SIZE, Image.Resampling.LANCZOS).convert(
        "RGB"
    )
    second_normalized = second.resize(
        COMPARISON_SIZE,
        Image.Resampling.LANCZOS,
    ).convert("RGB")
    difference = ImageChops.difference(first_normalized, second_normalized)
    channel_means = ImageStat.Stat(difference).mean
    normalized_difference = sum(channel_means) / (len(channel_means) * 255)
    return round(max(0.0, 1.0 - normalized_difference), 6)


def _validate_evidence_geometry(
    evidence: EvidenceBundle,
    width: int,
    height: int,
) -> None:
    coordinate_system = evidence.metadata.get("coordinateSystem")
    bounds = coordinate_system.get("bounds") if isinstance(coordinate_system, dict) else None
    if not isinstance(bounds, dict) or (
        bounds.get("xMinimum") != 0
        or bounds.get("yMinimum") != 0
        or bounds.get("xMaximumExclusive") != width
        or bounds.get("yMaximumExclusive") != height
    ):
        raise GameDriverError(
            "evidenceGeometryMismatch",
            "Observation image dimensions do not match evidence coordinate bounds.",
            3,
        )

    window = evidence.metadata.get("window")
    client_rect = window.get("clientRectOnScreen") if isinstance(window, dict) else None
    if not isinstance(client_rect, dict) or (
        client_rect.get("width") != width or client_rect.get("height") != height
    ):
        raise GameDriverError(
            "evidenceGeometryMismatch",
            "Observation image dimensions do not match the captured client rectangle.",
            3,
        )


def _page_result(
    match: PageMatch,
    width: int,
    height: int,
    evidence_oracle_eligible: bool,
) -> dict[str, object]:
    result: dict[str, object] = {
        "id": match.page.id,
        "kind": match.page.kind,
        "score": match.score,
        "rankingScore": match.ranking_score,
        "matchedAnchorCount": match.matched_anchor_count,
        "requiredAnchorCount": match.page.minimum_matched_anchors,
        "anchors": [_anchor_result(anchor) for anchor in match.anchors],
        "clientSize": {"width": width, "height": height},
    }
    if match.page.view_states:
        result["viewState"] = {
            "status": (
                "ambiguous"
                if match.view_state_ambiguous
                else ("matched" if match.view_state is not None else "unknown")
            ),
            "oracleEligible": (
                match.view_state is not None and evidence_oracle_eligible
            ),
            "state": (
                _view_state_result(match.view_state)
                if match.view_state is not None
                else None
            ),
            "candidates": [
                _view_state_candidate(candidate)
                for candidate in match.view_state_candidates
            ],
        }
    return result


def _page_candidate(match: PageMatch) -> dict[str, object]:
    result: dict[str, object] = {
        "id": match.page.id,
        "kind": match.page.kind,
        "score": match.score,
        "rankingScore": match.ranking_score,
        "matched": match.matched,
        "matchedAnchorCount": match.matched_anchor_count,
        "requiredAnchorCount": match.page.minimum_matched_anchors,
        "anchors": [_anchor_result(anchor) for anchor in match.anchors],
    }
    if match.page.view_states:
        result["viewStateStatus"] = (
            "ambiguous"
            if match.view_state_ambiguous
            else ("matched" if match.view_state is not None else "unknown")
        )
        result["viewStateId"] = (
            match.view_state.view_state.id if match.view_state is not None else None
        )
    return result


def _view_state_result(match: ViewStateMatch) -> dict[str, object]:
    return {
        "id": match.view_state.id,
        "score": match.score,
        "matchedAnchorCount": match.matched_anchor_count,
        "requiredAnchorCount": match.view_state.minimum_matched_anchors,
        "anchorIds": list(match.view_state.anchor_ids),
    }


def _view_state_candidate(match: ViewStateMatch) -> dict[str, object]:
    result = _view_state_result(match)
    result["matched"] = match.matched
    return result


def _anchor_result(anchor: AnchorMatch) -> dict[str, object]:
    return {
        "id": anchor.id,
        "score": anchor.score,
        "minimumScore": anchor.minimum_score,
        "matched": anchor.matched,
        "pageAnchor": anchor.page_anchor,
    }


def _element_results(
    match: PageMatch,
    width: int,
    height: int,
) -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    anchor_matches = {anchor.id: anchor for anchor in match.anchors}
    for element in match.page.elements:
        placement = _element_placement_for_match(match, element)
        if placement is None:
            result.append(
                {
                    "id": element.id,
                    "role": element.role,
                    "visibility": "viewStateUnknown",
                    "visible": None,
                    "confidence": None,
                    "detectorAnchorIds": [],
                    "viewStateId": None,
                    "boundsReference": None,
                    "boundsClient": None,
                    "actionPointClient": None,
                }
            )
            continue
        client_bounds = reference_rect_to_client(placement.bounds, width, height)
        action_point = None
        if placement.action_point is not None:
            action_x, action_y = reference_to_client(
                placement.action_point[0],
                placement.action_point[1],
                width,
                height,
            )
            action_point = {"x": action_x, "y": action_y}
        detector_matches = [
            anchor_matches[anchor_id] for anchor_id in placement.anchor_ids
        ]
        detected = bool(detector_matches) and all(item.matched for item in detector_matches)
        visibility = (
            "visible"
            if detected
            else ("notVisible" if detector_matches else "notEvaluated")
        )
        confidence = min((item.score for item in detector_matches), default=None)
        state_result = _element_state_result(
            getattr(placement, "states", ()),
            anchor_matches,
        )
        result.append(
            {
                "id": element.id,
                "role": element.role,
                "visibility": visibility,
                "visible": detected if detector_matches else None,
                "confidence": confidence,
                "detectorAnchorIds": list(placement.anchor_ids),
                "viewStateId": (
                    placement.view_state_id if element.placements else None
                ),
                "boundsReference": placement.bounds.to_dict(),
                "boundsClient": client_bounds.to_dict(),
                "actionPointClient": action_point,
                "state": state_result,
            }
        )
    return result


def _element_placement_for_match(match: PageMatch, element: VisualElement):
    if not element.placements:
        return element
    if match.view_state is None:
        return None
    return next(
        (
            placement
            for placement in element.placements
            if placement.view_state_id == match.view_state.view_state.id
        ),
        None,
    )


def _element_state_result(states, anchor_matches: dict[str, AnchorMatch]):
    if not states:
        return None
    candidates: list[dict[str, object]] = []
    for state in states:
        matches = [anchor_matches[anchor_id] for anchor_id in state.anchor_ids]
        matched = all(anchor.matched for anchor in matches)
        candidates.append(
            {
                "id": state.id,
                "matched": matched,
                "confidence": min(anchor.score for anchor in matches),
                "detectorAnchorIds": list(state.anchor_ids),
            }
        )
    matched_candidates = [candidate for candidate in candidates if candidate["matched"]]
    if len(matched_candidates) == 1:
        status = "matched"
        state_id = matched_candidates[0]["id"]
    elif len(matched_candidates) > 1:
        status = "ambiguous"
        state_id = None
    else:
        status = "unknown"
        state_id = None
    return {"status": status, "id": state_id, "candidates": candidates}
