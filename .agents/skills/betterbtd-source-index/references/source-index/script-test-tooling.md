# BetterBTD Script Test Tooling

Scenario validation and black-box orchestration around the Test API and Game Driver.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Game Driver Tooling](./game-driver-tooling.md)
- [BetterBTD Tests](./betterbtd-tests.md)
- [Script Test scenario protocol](../../../../../docs/developer/script-test-scenario.md)
- [Test API protocol](../../../../../docs/developer/test-api.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `tools/BetterBTD.ScriptTest` | 8 |
| `tools/BetterBTD.ScriptTest/betterbtd_script_test` | 8 |
| `tools/BetterBTD.ScriptTest/examples` | 1 |
| `tools/BetterBTD.ScriptTest/tests` | 6 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `tools/BetterBTD.ScriptTest`

| File | Description |
| --- | --- |
| [game-state-catalog.json](../../../../../tools/BetterBTD.ScriptTest/game-state-catalog.json) | External tool scenario, schema, catalog, or test data |
| [README.md](../../../../../tools/BetterBTD.ScriptTest/README.md) | External tool developer documentation |
| [requirements.txt](../../../../../tools/BetterBTD.ScriptTest/requirements.txt) | External tool configuration or runtime input |
| [scenario.schema.json](../../../../../tools/BetterBTD.ScriptTest/scenario.schema.json) | External tool scenario, schema, catalog, or test data |
| [script-test.ps1](../../../../../tools/BetterBTD.ScriptTest/script-test.ps1) | External tool entry point, setup, or test script |
| [setup.ps1](../../../../../tools/BetterBTD.ScriptTest/setup.ps1) | External tool entry point, setup, or test script |
| [test.ps1](../../../../../tools/BetterBTD.ScriptTest/test.ps1) | External tool entry point, setup, or test script |
| [validate-scenario.ps1](../../../../../tools/BetterBTD.ScriptTest/validate-scenario.ps1) | External tool entry point, setup, or test script |

### `tools/BetterBTD.ScriptTest/betterbtd_script_test`

| File | Description |
| --- | --- |
| [__init__.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/__init__.py) | External black-box tool implementation or test |
| [__main__.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/__main__.py) | External black-box tool implementation or test; primary symbols: _validation_parser, _command_parser, main, _run_validation, _write_json |
| [api_client.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/api_client.py) | External black-box tool implementation or test; primary symbols: __init__, __str__, __repr__, redact_text, health |
| [artifacts.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/artifacts.py) | External black-box tool implementation or test; primary symbols: new_run_id, create_session, load_session, update_manifest, atomic_write_json |
| [game_driver_client.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/game_driver_client.py) | External black-box tool implementation or test; primary symbols: recognition, reference, __init__, validate_catalog, observe |
| [orchestrator.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/orchestrator.py) | External black-box tool implementation or test; primary symbols: __init__, cancel_and_gate, run_act_and_assert, verify_recover_target, _require_idle_health |
| [predicates.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/predicates.py) | External black-box tool implementation or test; primary symbols: to_dict, evaluate_predicate, _evaluate_page, _evaluate_view_state, _evaluate_element_visibility |
| [scenario.py](../../../../../tools/BetterBTD.ScriptTest/betterbtd_script_test/scenario.py) | External black-box tool implementation or test; primary symbols: __init__, capability_compatible, validate_scenario, validate_script_summary, load_scenario_document |

### `tools/BetterBTD.ScriptTest/examples`

| File | Description |
| --- | --- |
| [easy-standard-victory.scenario.json](../../../../../tools/BetterBTD.ScriptTest/examples/easy-standard-victory.scenario.json) | External tool scenario, schema, catalog, or test data |

### `tools/BetterBTD.ScriptTest/tests`

| File | Description |
| --- | --- |
| [test_api_client.py](../../../../../tools/BetterBTD.ScriptTest/tests/test_api_client.py) | External black-box tool implementation or test; primary symbols: __init__, open, test_base_url_accepts_only_numeric_loopback_http_root, test_token_and_timeout_are_validated_without_disclosure, test_health_builds_authenticated_get_request |
| [test_artifacts.py](../../../../../tools/BetterBTD.ScriptTest/tests/test_artifacts.py) | External black-box tool implementation or test; primary symbols: test_new_run_id_is_protocol_safe, test_redact_removes_sensitive_keys_bearers_and_exact_secrets, test_redact_applies_client_owned_text_redactor_recursively, test_atomic_write_json_replaces_complete_document, test_create_and_load_session_binds_scenario_hash |
| [test_game_driver_client.py](../../../../../tools/BetterBTD.ScriptTest/tests/test_game_driver_client.py) | External black-box tool implementation or test; primary symbols: test_catalog_parses_one_json_object, test_game_driver_environment_does_not_inherit_credentials, test_act_and_assert_cannot_activate_the_window, test_nonzero_driver_exit_is_rejected, test_observation_passes_png_output_then_recognizes_adjacent_metadata |
| [test_orchestrator.py](../../../../../tools/BetterBTD.ScriptTest/tests/test_orchestrator.py) | External black-box tool implementation or test; primary symbols: test_lost_execute_response_preserves_act_starting_for_cleanup, test_cancel_and_gate_can_adopt_same_script_after_lost_execute_response, test_cancel_and_gate_rejects_operation_outside_execute_handoff, test_cancel_and_gate_stops_persisted_operation_before_recover, test_completed_api_and_independent_victory_pass |
| [test_predicates.py](../../../../../tools/BetterBTD.ScriptTest/tests/test_predicates.py) | External black-box tool implementation or test; primary symbols: test_result_is_immutable_and_serializable, test_page_equals_and_one_of_are_evaluated, test_view_state_requires_page_and_oracle_eligible_matched_state, test_element_visible_distinguishes_absent_and_unevaluated, test_element_state_equals_requires_a_unique_matched_state |
| [test_scenario.py](../../../../../tools/BetterBTD.ScriptTest/tests/test_scenario.py) | External black-box tool implementation or test; primary symbols: test_example_is_valid_and_compatible_with_current_capabilities, test_unknown_version_is_rejected, test_unknown_property_is_rejected, test_act_cannot_grant_game_driver_input, test_all_requires_a_positive_assertion |
