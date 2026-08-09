from copy import deepcopy
from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
import json
from pathlib import Path
import re
import tempfile
import unittest

from betterbtd_script_test.__main__ import main
from betterbtd_script_test.scenario import (
    ScenarioValidationError,
    _build_catalog_index,
    validate_scenario,
    validate_script_summary,
)


TOOL_ROOT = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOL_ROOT.parents[1]
EXAMPLE_PATH = TOOL_ROOT / "examples" / "easy-standard-victory.scenario.json"


class ScenarioValidationTests(unittest.TestCase):
    def test_example_is_valid_and_compatible_with_current_capabilities(self) -> None:
        result = validate_scenario(EXAMPLE_PATH)

        self.assertTrue(result.capability_compatible)
        self.assertEqual(
            REPOSITORY_ROOT / "test-scripts" / "easy-standard-victory.json",
            result.script_path,
        )
        self.assertEqual(
            REPOSITORY_ROOT / "artifacts" / "script-tests" / "{scenarioId}" / "{runId}",
            result.artifact_directory,
        )

    def test_unknown_version_is_rejected(self) -> None:
        document = self._example()
        document["schemaVersion"] = 2

        error = self._validate_invalid(document)

        self.assertIn("schemaVersion", str(error))

    def test_unknown_property_is_rejected(self) -> None:
        document = self._example()
        document["act"]["clickDuringAct"] = True

        error = self._validate_invalid(document)

        self.assertIn("clickDuringAct", str(error))

    def test_act_cannot_grant_game_driver_input(self) -> None:
        document = self._example()
        document["act"]["gameDriverInput"] = "Allowed"

        error = self._validate_invalid(document)

        self.assertIn("ObserveOnly", str(error))

    def test_all_requires_a_positive_assertion(self) -> None:
        document = self._example()
        document["assert"]["all"] = []

        error = self._validate_invalid(document)

        self.assertIn("should be non-empty", str(error))

    def test_predicate_ids_are_unique_across_all_phases(self) -> None:
        document = self._example()
        document["assert"]["all"][0]["id"] = "arrange-in-level"

        error = self._validate_invalid(document)

        self.assertIn("duplicates predicate id", str(error))

    def test_arrange_requires_a_page_predicate(self) -> None:
        document = self._example()
        document["arrange"]["readyWhen"] = [document["arrange"]["readyWhen"][1]]

        error = self._validate_invalid(document)

        self.assertIn("must contain a Page predicate", str(error))

    def test_view_state_must_belong_to_declared_page(self) -> None:
        document = self._example()
        document["arrange"]["readyWhen"][1]["pageId"] = "mainMenu"

        error = self._validate_invalid(document)

        self.assertIn("belongs to page 'inLevel'", str(error))

    def test_unknown_catalog_page_is_rejected(self) -> None:
        document = self._example()
        document["assert"]["all"][0] = {
            "id": "unknown-page",
            "quantifier": "Eventually",
            "observationWindow": "Assert",
            "kind": "Page",
            "operator": "Equals",
            "pageId": "missingPage",
        }

        error = self._validate_invalid(document)

        self.assertIn("unknown page 'missingPage'", str(error))

    def test_element_visibility_requires_an_independent_detector(self) -> None:
        document = self._example()
        document["requiredCapabilities"].append("ElementVisibility")
        document["assert"]["all"][0] = {
            "id": "cash-visible",
            "quantifier": "Eventually",
            "observationWindow": "ActAndAssert",
            "kind": "Element",
            "operator": "Visible",
            "elementId": "inLevel.cash",
        }

        error = self._validate_invalid(document)

        self.assertIn("has no independent visibility detector", str(error))

    def test_element_state_is_checked_against_catalog(self) -> None:
        document = self._example()
        document["requiredCapabilities"].extend(
            ["ElementVisibility", "ElementState"]
        )
        document["assert"]["all"][0] = {
            "id": "double-cash-state",
            "quantifier": "Eventually",
            "observationWindow": "Assert",
            "kind": "ElementState",
            "operator": "Equals",
            "elementId": "mapSelect.doubleCash",
            "state": "missing",
        }

        error = self._validate_invalid(document)

        self.assertIn("unknown state 'missing'", str(error))

    def test_supported_numeric_predicate_is_valid_and_compatible(self) -> None:
        document = self._numeric_document()

        result = self._validate(document)

        self.assertTrue(result.capability_compatible)
        self.assertEqual(frozenset(), result.missing_capabilities)

    def test_unsupported_value_element_is_rejected_as_numeric_oracle(self) -> None:
        for element_id in (
            "defeatSummary.roundReached",
            "victorySummary.reward",
        ):
            with self.subTest(element_id=element_id):
                document = self._numeric_document()
                document["assert"]["all"][-1]["elementId"] = element_id

                error = self._validate_invalid(document)

                self.assertIn(
                    f"element {element_id!r} has no independent numeric recognition",
                    str(error),
                )

    def test_victory_and_round_can_use_independent_observation_windows(self) -> None:
        document = self._numeric_document()

        result = self._validate(document)

        self.assertTrue(result.capability_compatible)
        self.assertEqual(
            ["Assert", "ActAndAssert"],
            [item["observationWindow"] for item in document["assert"]["all"]],
        )

    def test_predicate_capabilities_must_be_declared(self) -> None:
        document = self._numeric_document()
        document["requiredCapabilities"].remove("ElementNumber")

        error = self._validate_invalid(document)

        self.assertIn("must declare capabilities", str(error))
        self.assertIn("ElementNumber", str(error))

    def test_positive_assertion_requires_explicit_timing(self) -> None:
        document = self._example()
        del document["assert"]["all"][0]["observationWindow"]

        error = self._validate_invalid(document)

        self.assertIn("observationWindow is required", str(error))

    def test_assertion_timing_is_not_allowed_in_other_phases(self) -> None:
        document = self._example()
        document["assert"]["neverObserved"][0]["quantifier"] = "Eventually"

        error = self._validate_invalid(document)

        self.assertIn("only valid in $.assert.all", str(error))

    def test_script_path_cannot_escape_repository(self) -> None:
        document = self._example()
        document["script"]["path"] = "../../../../../../outside.json"

        error = self._validate_invalid(document)

        self.assertIn("script.path must resolve within the repository", str(error))

    def test_game_state_uses_stable_betterbtd_enum_names(self) -> None:
        cases = (
            ("map", "TypoMap"),
            ("difficulty", "Impossible"),
            ("mode", "NotAMode"),
            ("hero", "Nobody"),
        )
        for field, value in cases:
            with self.subTest(field=field):
                document = self._example()
                document["arrange"]["gameState"][field] = value

                error = self._validate_invalid(document)

                self.assertIn(
                    f"arrange.gameState.{field} has unknown value", str(error)
                )

    def test_mode_must_belong_to_selected_difficulty(self) -> None:
        document = self._example()
        document["arrange"]["gameState"]["mode"] = "CHIMPS"

        error = self._validate_invalid(document)

        self.assertIn("'CHIMPS' is not valid for difficulty 'Easy'", str(error))

    def test_artifact_directory_must_be_under_ignored_artifacts_root(self) -> None:
        document = self._example()
        document["failureArtifacts"]["directory"] = "reports/run"

        error = self._validate_invalid(document)

        self.assertIn("failureArtifacts.directory", str(error))

    def test_artifact_directory_cannot_traverse_out_of_artifacts(self) -> None:
        document = self._example()
        document["failureArtifacts"]["directory"] = (
            "artifacts/../.git/objects/{scenarioId}/{runId}"
        )

        error = self._validate_invalid(document)

        self.assertIn("failureArtifacts.directory", str(error))

    def test_artifact_directory_rejects_unknown_or_missing_placeholders(self) -> None:
        for directory in (
            "artifacts/{unknown}/{runId}",
            "artifacts/script-tests/{scenarioId}",
            "artifacts/{scenarioId}/{runId}/..",
        ):
            with self.subTest(directory=directory):
                document = self._example()
                document["failureArtifacts"]["directory"] = directory

                error = self._validate_invalid(document)

                self.assertIn("failureArtifacts.directory", str(error))

    def test_failure_artifact_requirements_cannot_be_disabled(self) -> None:
        document = self._example()
        document["failureArtifacts"]["testApiLogs"] = False

        error = self._validate_invalid(document)

        self.assertIn("testApiLogs", str(error))

    def test_secrets_and_dynamic_script_hash_are_forbidden_in_extensions(self) -> None:
        for field in (
            "token",
            "apiToken",
            "testApiToken",
            "bearerToken",
            "accessToken",
            "authorization",
            "credential",
            "password",
            "secret",
            "apiKey",
            "expectedSha256",
        ):
            with self.subTest(field=field):
                document = self._example()
                document["extensions"] = {field: "must-not-be-persisted"}

                error = self._validate_invalid(document)

                self.assertIn("is forbidden in scenario files", str(error))

    def test_credential_like_extension_values_are_rejected(self) -> None:
        document = self._example()
        document["extensions"] = {"note": "Bearer must-not-be-persisted"}

        error = self._validate_invalid(document)

        self.assertIn("contains credential-like content", str(error))

    def test_recover_requires_an_independently_observed_target(self) -> None:
        document = self._example()
        document["recover"]["targetWhen"] = []

        error = self._validate_invalid(document)

        self.assertIn("targetWhen", str(error))

    def test_script_validation_summary_must_match_arranged_game_state(self) -> None:
        result = validate_scenario(EXAMPLE_PATH)
        summary = {
            "map": "MonkeyMeadow",
            "difficulty": "Easy",
            "mode": "Standard",
            "hero": "Quincy",
        }

        validate_script_summary(result.expected_game_state, summary)
        summary["map"] = "Logs"

        with self.assertRaises(ScenarioValidationError) as context:
            validate_script_summary(result.expected_game_state, summary)

        self.assertIn("script map is 'Logs'", str(context.exception))

    def test_weak_game_driver_catalog_is_rejected(self) -> None:
        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index({"pages": [{"id": "mainMenu"}]})

        error = str(context.exception)
        self.assertIn("schemaVersion", error)
        self.assertIn("catalogId", error)
        self.assertIn("no anchors", error)
        self.assertIn("positiveHoldout", error)

    def test_match_groups_count_as_one_page_anchor(self) -> None:
        catalog = self._game_driver_catalog()
        in_level = next(page for page in catalog["pages"] if page["id"] == "inLevel")
        in_level["minimumMatchedAnchors"] = 5

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn("page anchor group count", str(context.exception))

    def test_match_group_requires_page_anchor_and_page_scoped_id(self) -> None:
        cases = (
            ("inLevel.healthIcon", "inLevel.healthMode", "requires a page anchor"),
            ("inLevel.powersAvailable", "sandbox.powersMode", "must start with inLevel."),
        )
        for anchor_id, match_group, expected_error in cases:
            with self.subTest(anchor=anchor_id):
                catalog = self._game_driver_catalog()
                in_level = next(
                    page for page in catalog["pages"] if page["id"] == "inLevel"
                )
                anchor = next(
                    item for item in in_level["anchors"] if item["id"] == anchor_id
                )
                anchor["matchGroup"] = match_group

                with self.assertRaises(ScenarioValidationError) as context:
                    _build_catalog_index(catalog)

                self.assertIn(expected_error, str(context.exception))

    def test_malformed_number_declaration_is_not_indexed(self) -> None:
        catalog = self._game_driver_catalog()
        in_level = next(page for page in catalog["pages"] if page["id"] == "inLevel")
        round_element = next(
            element
            for element in in_level["elements"]
            if element["id"] == "inLevel.round"
        )
        round_element["number"]["format"] = "decimal"

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn("number has an invalid format", str(context.exception))

    def test_number_model_requires_all_digit_glyphs(self) -> None:
        catalog = self._game_driver_catalog()
        catalog["numberModels"][0]["glyphs"] = []

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn("must contain digits 0 through 9", str(context.exception))

    def test_number_model_rejects_invalid_matching_parameters(self) -> None:
        cases = (
            ("minimumScore", 1.01, "invalid minimumScore"),
            ("minimumMargin", -0.01, "invalid minimumMargin"),
        )
        for field, value, expected_error in cases:
            with self.subTest(field=field):
                catalog = self._game_driver_catalog()
                catalog["numberModels"][0][field] = value

                with self.assertRaises(ScenarioValidationError) as context:
                    _build_catalog_index(catalog)

                self.assertIn(expected_error, str(context.exception))

    def test_number_glyph_requires_provenance_fields(self) -> None:
        cases = (
            ("sourceEvidence", "", "invalid sourceEvidence path"),
            ("sourceEvidenceId", "", "invalid sourceEvidenceId"),
            ("sourceImageSha256", "invalid", "invalid sourceImageSha256"),
            ("templateSha256", "invalid", "invalid templateSha256"),
        )
        for field, value, expected_error in cases:
            with self.subTest(field=field):
                catalog = self._game_driver_catalog()
                catalog["numberModels"][0]["glyphs"][0][field] = value

                with self.assertRaises(ScenarioValidationError) as context:
                    _build_catalog_index(catalog)

                self.assertIn(expected_error, str(context.exception))

    def test_number_glyph_source_bounds_must_be_in_reference_space(self) -> None:
        catalog = self._game_driver_catalog()
        catalog["numberModels"][0]["glyphs"][0]["sourceBounds"] = {
            "x": 1915,
            "y": 0,
            "width": 10,
            "height": 10,
        }

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn("invalid sourceBounds", str(context.exception))

    def test_element_number_bounds_must_be_in_reference_space(self) -> None:
        catalog = self._game_driver_catalog()
        round_element = self._catalog_element(catalog, "inLevel", "inLevel.round")
        round_element["number"]["bounds"] = {
            "x": 1915,
            "y": 0,
            "width": 10,
            "height": 10,
        }

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn(
            "number bounds must be inside the reference space",
            str(context.exception),
        )

    def test_element_number_bounds_must_be_inside_element_bounds(self) -> None:
        catalog = self._game_driver_catalog()
        round_element = self._catalog_element(catalog, "inLevel", "inLevel.round")
        round_element["number"]["bounds"] = {
            "x": 0,
            "y": 100,
            "width": 10,
            "height": 10,
        }

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn(
            "number bounds must be inside element bounds",
            str(context.exception),
        )

    def test_element_number_does_not_support_placements(self) -> None:
        catalog = self._game_driver_catalog()
        round_element = self._catalog_element(catalog, "inLevel", "inLevel.round")
        round_element["placements"] = [
            {
                "viewStateId": "inLevel.roundIdle",
                "bounds": round_element["bounds"],
                "anchorIds": [],
            }
        ]

        with self.assertRaises(ScenarioValidationError) as context:
            _build_catalog_index(catalog)

        self.assertIn("number does not support placements", str(context.exception))

    def test_duplicate_json_properties_are_rejected(self) -> None:
        source = EXAMPLE_PATH.read_text(encoding="utf-8")
        duplicate = source.replace(
            '"schemaVersion": 1,',
            '"schemaVersion": 1,\n  "schemaVersion": 1,',
            1,
        )

        with tempfile.TemporaryDirectory(dir=TOOL_ROOT) as temporary_directory:
            scenario_path = Path(temporary_directory) / "duplicate.scenario.json"
            scenario_path.write_text(duplicate, encoding="utf-8")

            with self.assertRaises(ScenarioValidationError) as context:
                validate_scenario(scenario_path)

        self.assertIn("duplicate JSON property", str(context.exception))

    def test_non_standard_json_constants_are_rejected(self) -> None:
        document = self._example()
        document["extensions"] = {"measurement": float("nan")}

        error = self._validate_invalid(document)

        self.assertIn("non-standard JSON constant 'NaN'", str(error))

    def test_game_state_catalog_matches_betterbtd_enums(self) -> None:
        catalog = json.loads(
            (TOOL_ROOT / "game-state-catalog.json").read_text(encoding="utf-8")
        )
        map_definitions = REPOSITORY_ROOT / "BetterBTD" / "Models" / "GameElements"

        self.assertEqual(
            self._enum_members(map_definitions / "MapDefinitions.cs", "GameMapType"),
            catalog["maps"],
        )
        self.assertEqual(
            self._enum_members(
                map_definitions / "MapDefinitions.cs", "StageDifficulty"
            ),
            catalog["difficulties"],
        )
        self.assertEqual(
            self._enum_members(map_definitions / "MapDefinitions.cs", "StageMode"),
            catalog["modes"],
        )
        self.assertEqual(
            self._enum_members(map_definitions / "HeroType.cs", "HeroType"),
            catalog["heroes"],
        )

    def test_cli_accepts_supported_numeric_oracle(self) -> None:
        document = self._numeric_document()
        with tempfile.TemporaryDirectory(dir=TOOL_ROOT) as temporary_directory:
            scenario_path = Path(temporary_directory) / "numeric.scenario.json"
            scenario_path.write_text(json.dumps(document), encoding="utf-8")
            output = StringIO()
            errors = StringIO()

            with redirect_stdout(output), redirect_stderr(errors):
                exit_code = main([str(scenario_path)])

        response = json.loads(output.getvalue())
        self.assertEqual(0, exit_code)
        self.assertTrue(response["valid"])
        self.assertTrue(response["capabilityCompatible"])
        self.assertEqual([], response["missingCapabilities"])
        self.assertEqual("", errors.getvalue())

    def test_current_catalog_indexes_all_independent_numeric_oracles(self) -> None:
        index = _build_catalog_index(self._game_driver_catalog())

        self.assertEqual(
            {
                "inLevel.health",
                "inLevel.cash",
                "inLevel.round",
                "sandbox.health",
                "sandbox.cash",
                "sandbox.round",
                "sandboxTower.health",
                "sandboxTower.cash",
                "sandboxTower.round",
            },
            set(index.numeric_elements),
        )

    def _numeric_document(self) -> dict[str, object]:
        document = self._example()
        document["requiredCapabilities"].append("ElementNumber")
        document["assert"]["all"].append(
            {
                "id": "round-at-least-40",
                "quantifier": "Eventually",
                "observationWindow": "ActAndAssert",
                "kind": "ElementNumber",
                "operator": "GreaterThanOrEqual",
                "elementId": "inLevel.round",
                "value": 40,
            }
        )
        return document

    @staticmethod
    def _game_driver_catalog() -> dict[str, object]:
        catalog_path = (
            REPOSITORY_ROOT
            / "tools"
            / "BetterBTD.GameDriver"
            / "visual-baselines"
            / "catalog.json"
        )
        return json.loads(catalog_path.read_text(encoding="utf-8"))

    @staticmethod
    def _catalog_element(
        catalog: dict[str, object],
        page_id: str,
        element_id: str,
    ) -> dict[str, object]:
        page = next(page for page in catalog["pages"] if page["id"] == page_id)
        return next(
            element for element in page["elements"] if element["id"] == element_id
        )

    @staticmethod
    def _enum_members(path: Path, enum_name: str) -> list[str]:
        source = path.read_text(encoding="utf-8")
        match = re.search(
            rf"public enum {re.escape(enum_name)}\s*\{{(?P<body>.*?)\}}",
            source,
            re.DOTALL,
        )
        if match is None:
            raise AssertionError(f"enum {enum_name} was not found in {path}")
        return [
            member.group(1)
            for member in re.finditer(
                r"^\s*([A-Za-z][A-Za-z0-9]*)\s*(?:=\s*[^,]+)?\s*,?\s*$",
                match.group("body"),
                re.MULTILINE,
            )
        ]

    @staticmethod
    def _example() -> dict[str, object]:
        return deepcopy(json.loads(EXAMPLE_PATH.read_text(encoding="utf-8")))

    def _validate(self, document: dict[str, object], **options: object):
        with tempfile.TemporaryDirectory(dir=TOOL_ROOT) as temporary_directory:
            scenario_path = Path(temporary_directory) / "test.scenario.json"
            scenario_path.write_text(json.dumps(document), encoding="utf-8")
            return validate_scenario(scenario_path, **options)

    def _validate_invalid(self, document: dict[str, object]) -> ScenarioValidationError:
        with self.assertRaises(ScenarioValidationError) as context:
            self._validate(document)
        return context.exception


if __name__ == "__main__":
    unittest.main()
