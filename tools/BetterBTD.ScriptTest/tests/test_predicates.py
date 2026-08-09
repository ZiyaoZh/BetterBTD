from dataclasses import FrozenInstanceError
import json
import unittest

from betterbtd_script_test.predicates import (
    PredicateEvaluationResult,
    evaluate_predicate,
)


class PredicateEvaluationTests(unittest.TestCase):
    def test_result_is_immutable_and_serializable(self) -> None:
        result = PredicateEvaluationResult(True, False, "pageMismatch")

        self.assertEqual(
            {"evaluable": True, "matched": False, "reason": "pageMismatch"},
            result.to_dict(),
        )
        self.assertEqual(
            '{"evaluable": true, "matched": false, "reason": "pageMismatch"}',
            json.dumps(result.to_dict()),
        )
        with self.assertRaises(FrozenInstanceError):
            result.matched = True  # type: ignore[misc]

    def test_page_equals_and_one_of_are_evaluated(self) -> None:
        recognition = self._recognition()
        cases = (
            ({"kind": "Page", "operator": "Equals", "pageId": "inLevel"}, True),
            ({"kind": "Page", "operator": "Equals", "pageId": "mainMenu"}, False),
            (
                {
                    "kind": "Page",
                    "operator": "OneOf",
                    "pageIds": ["mainMenu", "inLevel"],
                },
                True,
            ),
            (
                {
                    "kind": "Page",
                    "operator": "OneOf",
                    "pageIds": ["victorySummary", "defeatSummary"],
                },
                False,
            ),
        )

        for predicate, expected_match in cases:
            with self.subTest(predicate=predicate):
                result = evaluate_predicate(predicate, recognition)

                self.assertTrue(result.evaluable)
                self.assertEqual(expected_match, result.matched)
                self.assertEqual(None if expected_match else "pageMismatch", result.reason)

    def test_view_state_requires_page_and_oracle_eligible_matched_state(self) -> None:
        recognition = self._recognition()
        predicate = {
            "kind": "ViewState",
            "operator": "Equals",
            "pageId": "inLevel",
            "viewStateId": "inLevel.roundReady",
        }

        self.assertEqual(
            PredicateEvaluationResult(True, True, None),
            evaluate_predicate(predicate, recognition),
        )

        other_page = dict(predicate, pageId="mainMenu")
        self.assertEqual(
            PredicateEvaluationResult(True, False, "pageMismatch"),
            evaluate_predicate(other_page, recognition),
        )

        other_state = dict(predicate, viewStateId="inLevel.roundActive")
        self.assertEqual(
            PredicateEvaluationResult(True, False, "viewStateMismatch"),
            evaluate_predicate(other_state, recognition),
        )

        for status, oracle_eligible, reason in (
            ("unknown", False, "viewStateUnknown"),
            ("ambiguous", False, "viewStateAmbiguous"),
            ("matched", False, "viewStateNotOracleEligible"),
        ):
            with self.subTest(status=status, oracle_eligible=oracle_eligible):
                document = self._recognition()
                view_state = document["recognition"]["page"]["viewState"]
                view_state["status"] = status
                view_state["oracleEligible"] = oracle_eligible

                self.assertEqual(
                    PredicateEvaluationResult(False, False, reason),
                    evaluate_predicate(predicate, document),
                )

    def test_element_visible_distinguishes_absent_and_unevaluated(self) -> None:
        predicate = {
            "kind": "Element",
            "operator": "Visible",
            "elementId": "inLevel.settings",
        }

        self.assertTrue(evaluate_predicate(predicate, self._recognition()).matched)

        not_visible = self._recognition()
        element = not_visible["recognition"]["elements"][0]
        element.update({"visibility": "notVisible", "visible": False})
        self.assertEqual(
            PredicateEvaluationResult(True, False, "elementNotVisible"),
            evaluate_predicate(predicate, not_visible),
        )

        for visibility, reason in (
            ("notEvaluated", "elementVisibilityNotEvaluated"),
            ("viewStateUnknown", "elementVisibilityViewStateUnknown"),
        ):
            with self.subTest(visibility=visibility):
                document = self._recognition()
                document["recognition"]["elements"][0].update(
                    {"visibility": visibility, "visible": None}
                )

                self.assertEqual(
                    PredicateEvaluationResult(False, False, reason),
                    evaluate_predicate(predicate, document),
                )

    def test_element_state_equals_requires_a_unique_matched_state(self) -> None:
        predicate = {
            "kind": "ElementState",
            "operator": "Equals",
            "elementId": "inLevel.settings",
            "state": "enabled",
        }

        self.assertTrue(evaluate_predicate(predicate, self._recognition()).matched)

        mismatch = dict(predicate, state="disabled")
        self.assertEqual(
            PredicateEvaluationResult(True, False, "elementStateMismatch"),
            evaluate_predicate(mismatch, self._recognition()),
        )

        for status, reason in (
            ("unknown", "elementStateUnknown"),
            ("ambiguous", "elementStateAmbiguous"),
        ):
            with self.subTest(status=status):
                document = self._recognition()
                state = document["recognition"]["elements"][0]["state"]
                state.update({"status": status, "id": None})

                self.assertEqual(
                    PredicateEvaluationResult(False, False, reason),
                    evaluate_predicate(predicate, document),
                )

    def test_element_number_supports_all_scenario_operators(self) -> None:
        recognition = self._recognition()
        cases = (
            ("Equals", 40, True),
            ("Equals", 41, False),
            ("GreaterThanOrEqual", 40, True),
            ("GreaterThanOrEqual", 41, False),
            ("LessThanOrEqual", 40, True),
            ("LessThanOrEqual", 39, False),
        )

        for operator, value, expected_match in cases:
            with self.subTest(operator=operator, value=value):
                predicate = {
                    "kind": "ElementNumber",
                    "operator": operator,
                    "elementId": "inLevel.round",
                    "value": value,
                }
                result = evaluate_predicate(predicate, recognition)

                self.assertTrue(result.evaluable)
                self.assertEqual(expected_match, result.matched)
                self.assertEqual(
                    None if expected_match else "elementNumberMismatch",
                    result.reason,
                )

    def test_element_number_unknown_ambiguous_and_non_oracle_fail_closed(self) -> None:
        predicate = {
            "kind": "ElementNumber",
            "operator": "Equals",
            "elementId": "inLevel.round",
            "value": 40,
        }
        cases = (
            ("unknown", False, "elementNumberUnknown"),
            ("ambiguous", False, "elementNumberAmbiguous"),
            ("matched", False, "elementNumberNotOracleEligible"),
        )

        for status, oracle_eligible, reason in cases:
            with self.subTest(status=status, oracle_eligible=oracle_eligible):
                document = self._recognition()
                number = document["recognition"]["elements"][1]["number"]
                number.update(
                    {
                        "status": status,
                        "oracleEligible": oracle_eligible,
                        "value": None if status != "matched" else 40,
                    }
                )

                self.assertEqual(
                    PredicateEvaluationResult(False, False, reason),
                    evaluate_predicate(predicate, document),
                )

    def test_top_level_unknown_ambiguous_and_non_oracle_fail_closed(self) -> None:
        predicate = {"kind": "Page", "operator": "Equals", "pageId": "inLevel"}
        cases = (
            ("unknown", False, "recognitionUnknown"),
            ("ambiguous", False, "recognitionAmbiguous"),
            ("matched", False, "recognitionNotOracleEligible"),
        )

        for status, oracle_eligible, reason in cases:
            with self.subTest(status=status, oracle_eligible=oracle_eligible):
                document = self._recognition()
                document["recognition"].update(
                    {"status": status, "oracleEligible": oracle_eligible}
                )

                self.assertEqual(
                    PredicateEvaluationResult(False, False, reason),
                    evaluate_predicate(predicate, document),
                )

    def test_missing_malformed_and_unsupported_input_never_raises(self) -> None:
        valid_predicate = {
            "kind": "ElementNumber",
            "operator": "Equals",
            "elementId": "inLevel.round",
            "value": 40,
        }
        cases = (
            (None, self._recognition(), "predicateMalformed"),
            (valid_predicate, None, "recognitionDocumentMalformed"),
            (valid_predicate, {}, "recognitionMalformed"),
            (
                {"kind": "FuturePredicate", "operator": "Equals"},
                self._recognition(),
                "predicateUnsupported",
            ),
            (
                dict(valid_predicate, value=True),
                self._recognition(),
                "predicateMalformed",
            ),
        )

        for predicate, document, reason in cases:
            with self.subTest(reason=reason):
                result = evaluate_predicate(predicate, document)  # type: ignore[arg-type]

                self.assertEqual(PredicateEvaluationResult(False, False, reason), result)

        missing_element = self._recognition()
        missing_element["recognition"]["elements"] = []
        self.assertEqual(
            PredicateEvaluationResult(False, False, "elementNotFound"),
            evaluate_predicate(valid_predicate, missing_element),
        )

        malformed_value = self._recognition()
        malformed_value["recognition"]["elements"][1]["number"]["value"] = True
        self.assertEqual(
            PredicateEvaluationResult(False, False, "elementNumberMalformed"),
            evaluate_predicate(valid_predicate, malformed_value),
        )

    @staticmethod
    def _recognition() -> dict:
        return {
            "schemaVersion": 1,
            "recognition": {
                "status": "matched",
                "oracleEligible": True,
                "page": {
                    "id": "inLevel",
                    "viewState": {
                        "status": "matched",
                        "oracleEligible": True,
                        "state": {"id": "inLevel.roundReady"},
                    },
                },
                "elements": [
                    {
                        "id": "inLevel.settings",
                        "visibility": "visible",
                        "visible": True,
                        "state": {"status": "matched", "id": "enabled"},
                        "number": None,
                    },
                    {
                        "id": "inLevel.round",
                        "visibility": "notEvaluated",
                        "visible": None,
                        "state": None,
                        "number": {
                            "status": "matched",
                            "oracleEligible": True,
                            "value": 40,
                        },
                    },
                ],
            },
        }


if __name__ == "__main__":
    unittest.main()
