from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True, slots=True)
class PredicateEvaluationResult:
    evaluable: bool
    matched: bool
    reason: str | None

    def to_dict(self) -> dict[str, object]:
        return {
            "evaluable": self.evaluable,
            "matched": self.matched,
            "reason": self.reason,
        }


def evaluate_predicate(
    predicate: Mapping[str, Any],
    recognition_document: Mapping[str, Any],
) -> PredicateEvaluationResult:
    if not isinstance(predicate, Mapping):
        return _unevaluable("predicateMalformed")
    if not isinstance(recognition_document, Mapping):
        return _unevaluable("recognitionDocumentMalformed")

    recognition = recognition_document.get("recognition")
    if not isinstance(recognition, Mapping):
        return _unevaluable("recognitionMalformed")

    recognition_status = recognition.get("status")
    if recognition_status == "unknown":
        return _unevaluable("recognitionUnknown")
    if recognition_status == "ambiguous":
        return _unevaluable("recognitionAmbiguous")
    if recognition_status != "matched":
        return _unevaluable("recognitionStatusMalformed")

    oracle_eligible = recognition.get("oracleEligible")
    if oracle_eligible is False:
        return _unevaluable("recognitionNotOracleEligible")
    if oracle_eligible is not True:
        return _unevaluable("recognitionOracleEligibilityMalformed")

    page = recognition.get("page")
    if not isinstance(page, Mapping) or not _is_identifier(page.get("id")):
        return _unevaluable("recognitionPageMalformed")

    kind = predicate.get("kind")
    operator = predicate.get("operator")
    if not isinstance(kind, str) or not isinstance(operator, str):
        return _unevaluable("predicateMalformed")

    if kind == "Page":
        return _evaluate_page(predicate, operator, page["id"])
    if kind == "ViewState":
        return _evaluate_view_state(predicate, operator, page)
    if kind == "Element":
        return _evaluate_element_visibility(predicate, operator, recognition)
    if kind == "ElementState":
        return _evaluate_element_state(predicate, operator, recognition)
    if kind == "ElementNumber":
        return _evaluate_element_number(predicate, operator, recognition)
    return _unevaluable("predicateUnsupported")


def _evaluate_page(
    predicate: Mapping[str, Any],
    operator: str,
    actual_page_id: str,
) -> PredicateEvaluationResult:
    if operator == "Equals":
        expected_page_id = predicate.get("pageId")
        if not _is_identifier(expected_page_id):
            return _unevaluable("predicateMalformed")
        return _comparison(actual_page_id == expected_page_id, "pageMismatch")

    if operator == "OneOf":
        expected_page_ids = predicate.get("pageIds")
        if not isinstance(expected_page_ids, list) or (
            len(expected_page_ids) < 2
            or any(not _is_identifier(page_id) for page_id in expected_page_ids)
            or len(set(expected_page_ids)) != len(expected_page_ids)
        ):
            return _unevaluable("predicateMalformed")
        return _comparison(actual_page_id in expected_page_ids, "pageMismatch")

    return _unevaluable("predicateUnsupported")


def _evaluate_view_state(
    predicate: Mapping[str, Any],
    operator: str,
    page: Mapping[str, Any],
) -> PredicateEvaluationResult:
    expected_page_id = predicate.get("pageId")
    expected_view_state_id = predicate.get("viewStateId")
    if (
        operator != "Equals"
        or not _is_identifier(expected_page_id)
        or not _is_identifier(expected_view_state_id)
    ):
        return _unevaluable(
            "predicateUnsupported" if operator != "Equals" else "predicateMalformed"
        )

    if page["id"] != expected_page_id:
        return _unmatched("pageMismatch")

    view_state = page.get("viewState")
    if not isinstance(view_state, Mapping):
        return _unevaluable("viewStateMalformed")
    status = view_state.get("status")
    if status == "unknown":
        return _unevaluable("viewStateUnknown")
    if status == "ambiguous":
        return _unevaluable("viewStateAmbiguous")
    if status != "matched":
        return _unevaluable("viewStateStatusMalformed")

    oracle_eligible = view_state.get("oracleEligible")
    if oracle_eligible is False:
        return _unevaluable("viewStateNotOracleEligible")
    if oracle_eligible is not True:
        return _unevaluable("viewStateOracleEligibilityMalformed")

    state = view_state.get("state")
    if not isinstance(state, Mapping) or not _is_identifier(state.get("id")):
        return _unevaluable("viewStateMalformed")
    return _comparison(state["id"] == expected_view_state_id, "viewStateMismatch")


def _evaluate_element_visibility(
    predicate: Mapping[str, Any],
    operator: str,
    recognition: Mapping[str, Any],
) -> PredicateEvaluationResult:
    if operator != "Visible":
        return _unevaluable("predicateUnsupported")
    element_id = predicate.get("elementId")
    if not _is_identifier(element_id):
        return _unevaluable("predicateMalformed")

    element, error = _find_element(recognition, element_id)
    if error is not None:
        return _unevaluable(error)
    assert element is not None

    visibility = element.get("visibility")
    visible = element.get("visible")
    if visibility == "visible":
        if visible is not True:
            return _unevaluable("elementVisibilityMalformed")
        return _matched()
    if visibility == "notVisible":
        if visible is not False:
            return _unevaluable("elementVisibilityMalformed")
        return _unmatched("elementNotVisible")
    if visibility == "notEvaluated":
        return _unevaluable("elementVisibilityNotEvaluated")
    if visibility == "viewStateUnknown":
        return _unevaluable("elementVisibilityViewStateUnknown")
    return _unevaluable("elementVisibilityMalformed")


def _evaluate_element_state(
    predicate: Mapping[str, Any],
    operator: str,
    recognition: Mapping[str, Any],
) -> PredicateEvaluationResult:
    element_id = predicate.get("elementId")
    expected_state = predicate.get("state")
    if (
        operator != "Equals"
        or not _is_identifier(element_id)
        or not _is_identifier(expected_state)
    ):
        return _unevaluable(
            "predicateUnsupported" if operator != "Equals" else "predicateMalformed"
        )

    element, error = _find_element(recognition, element_id)
    if error is not None:
        return _unevaluable(error)
    assert element is not None

    state = element.get("state")
    if not isinstance(state, Mapping):
        return _unevaluable("elementStateMalformed")
    status = state.get("status")
    if status == "unknown":
        return _unevaluable("elementStateUnknown")
    if status == "ambiguous":
        return _unevaluable("elementStateAmbiguous")
    if status != "matched" or not _is_identifier(state.get("id")):
        return _unevaluable("elementStateMalformed")
    return _comparison(state["id"] == expected_state, "elementStateMismatch")


def _evaluate_element_number(
    predicate: Mapping[str, Any],
    operator: str,
    recognition: Mapping[str, Any],
) -> PredicateEvaluationResult:
    element_id = predicate.get("elementId")
    expected_value = predicate.get("value")
    if not _is_identifier(element_id) or not _is_integer(expected_value):
        return _unevaluable("predicateMalformed")
    if operator not in ("Equals", "GreaterThanOrEqual", "LessThanOrEqual"):
        return _unevaluable("predicateUnsupported")

    element, error = _find_element(recognition, element_id)
    if error is not None:
        return _unevaluable(error)
    assert element is not None

    number = element.get("number")
    if not isinstance(number, Mapping):
        return _unevaluable("elementNumberMalformed")
    status = number.get("status")
    if status == "unknown":
        return _unevaluable("elementNumberUnknown")
    if status == "ambiguous":
        return _unevaluable("elementNumberAmbiguous")
    if status != "matched":
        return _unevaluable("elementNumberStatusMalformed")

    oracle_eligible = number.get("oracleEligible")
    if oracle_eligible is False:
        return _unevaluable("elementNumberNotOracleEligible")
    if oracle_eligible is not True:
        return _unevaluable("elementNumberOracleEligibilityMalformed")

    actual_value = number.get("value")
    if not _is_integer(actual_value):
        return _unevaluable("elementNumberMalformed")

    if operator == "Equals":
        matched = actual_value == expected_value
    elif operator == "GreaterThanOrEqual":
        matched = actual_value >= expected_value
    else:
        matched = actual_value <= expected_value
    return _comparison(matched, "elementNumberMismatch")


def _find_element(
    recognition: Mapping[str, Any],
    element_id: str,
) -> tuple[Mapping[str, Any] | None, str | None]:
    elements = recognition.get("elements")
    if not isinstance(elements, list):
        return None, "recognitionElementsMalformed"

    matches: list[Mapping[str, Any]] = []
    for element in elements:
        if not isinstance(element, Mapping) or not _is_identifier(element.get("id")):
            return None, "recognitionElementsMalformed"
        if element["id"] == element_id:
            matches.append(element)
    if not matches:
        return None, "elementNotFound"
    if len(matches) != 1:
        return None, "elementAmbiguous"
    return matches[0], None


def _is_identifier(value: object) -> bool:
    return isinstance(value, str) and bool(value)


def _is_integer(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _matched() -> PredicateEvaluationResult:
    return PredicateEvaluationResult(evaluable=True, matched=True, reason=None)


def _unmatched(reason: str) -> PredicateEvaluationResult:
    return PredicateEvaluationResult(evaluable=True, matched=False, reason=reason)


def _unevaluable(reason: str) -> PredicateEvaluationResult:
    return PredicateEvaluationResult(evaluable=False, matched=False, reason=reason)


def _comparison(matched: bool, mismatch_reason: str) -> PredicateEvaluationResult:
    return _matched() if matched else _unmatched(mismatch_reason)
