# BetterBTD Game Driver Tooling

Independent Python and PowerShell tooling for observing and controlling a real BTD6 client.

[Back to BetterBTD Source Index](../source-index.md)

## Related Indexes

- [BetterBTD Script Test Tooling](./script-test-tooling.md)
- [BetterBTD Tests](./betterbtd-tests.md)
- [Game Driver developer protocol](../../../../../docs/developer/game-driver.md)

## Directory Summary

| Directory | Files |
| --- | ---: |
| `tools/BetterBTD.GameDriver` | 6 |
| `tools/BetterBTD.GameDriver/betterbtd_game_driver` | 14 |
| `tools/BetterBTD.GameDriver/tests` | 11 |

## File Inventory

Paths are relative to the repository root. Open the linked file and verify current behavior before editing.

### `tools/BetterBTD.GameDriver`

| File | Description |
| --- | --- |
| [btd6_game_driver.py](../../../../../tools/BetterBTD.GameDriver/btd6_game_driver.py) | External black-box tool implementation or test |
| [game-driver.ps1](../../../../../tools/BetterBTD.GameDriver/game-driver.ps1) | External tool entry point, setup, or test script |
| [README.md](../../../../../tools/BetterBTD.GameDriver/README.md) | External tool developer documentation |
| [requirements.txt](../../../../../tools/BetterBTD.GameDriver/requirements.txt) | External tool configuration or runtime input |
| [setup.ps1](../../../../../tools/BetterBTD.GameDriver/setup.ps1) | External tool entry point, setup, or test script |
| [test.ps1](../../../../../tools/BetterBTD.GameDriver/test.ps1) | External tool entry point, setup, or test script |

### `tools/BetterBTD.GameDriver/betterbtd_game_driver`

| File | Description |
| --- | --- |
| [__init__.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/__init__.py) | External black-box tool implementation or test |
| [__main__.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/__main__.py) | External black-box tool implementation or test |
| [baseline.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/baseline.py) | External black-box tool implementation or test; primary symbols: build_templates, _encode_png, _number_glyph_mask, _write_template |
| [cli.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/cli.py) | External black-box tool implementation or test; primary symbols: error, create_parser, _add_interaction_arguments, parse_args, main |
| [coordinates.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/coordinates.py) | External black-box tool implementation or test; primary symbols: reference_to_client, client_to_reference, client_to_screen, screen_to_client, reference_rect_to_client |
| [driver.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/driver.py) | External black-box tool implementation or test; primary symbols: __init__, window_api, list_windows, capture, _resolve_window |
| [errors.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/errors.py) | External black-box tool implementation or test; primary symbols: __init__ |
| [evidence.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/evidence.py) | External black-box tool implementation or test; primary symbols: read_evidence, evidence_reference, _read_bytes, _parse_json, _invalid_evidence |
| [interaction.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/interaction.py) | External black-box tool implementation or test; primary symbols: __init__, observe, click, click_point, scroll_point |
| [models.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/models.py) | External black-box tool implementation or test; primary symbols: right, bottom, area, contains, to_dict |
| [png.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/png.py) | External black-box tool implementation or test; primary symbols: encode_bgrx32, visible_pixel_sha256, analyze_bgrx32, _open_bgrx32, _validate_pixels |
| [vision.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/vision.py) | External black-box tool implementation or test; primary symbols: recognize_image, recognize_frame, write_annotation, _match_page, _match_view_states |
| [visual_catalog.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/visual_catalog.py) | External black-box tool implementation or test; primary symbols: load_visual_catalog, visual_catalog_summary, _parse_catalog, _parse_number_models, _validate_number_glyph_alpha_mask |
| [win32.py](../../../../../tools/BetterBTD.GameDriver/betterbtd_game_driver/win32.py) | External black-box tool implementation or test; primary symbols: keyboard_chord_is_unsafe, enable_per_monitor_v2, list_windows, callback, snapshot |

### `tools/BetterBTD.GameDriver/tests`

| File | Description |
| --- | --- |
| [test_baseline.py](../../../../../tools/BetterBTD.GameDriver/tests/test_baseline.py) | External black-box tool implementation or test; primary symbols: test_bundled_templates_rebuild_with_declared_provenance_and_hashes, test_schema_v3_rgb_number_templates_rebuild_with_legacy_hashes, test_late_validation_failure_writes_no_templates |
| [test_cli.py](../../../../../tools/BetterBTD.GameDriver/tests/test_cli.py) | External black-box tool implementation or test; primary symbols: test_no_arguments_selects_help, test_capture_defaults_to_real_btd6_window_titles_and_processes, test_explicit_process_name_does_not_apply_default_title_filter, test_hexadecimal_window_handle_is_supported, test_launch_cannot_be_combined_with_exact_handle |
| [test_coordinates.py](../../../../../tools/BetterBTD.GameDriver/tests/test_coordinates.py) | External black-box tool implementation or test; primary symbols: test_reference_center_scales_to_actual_client_center, test_reference_coordinate_is_clamped_to_half_open_client_bounds, test_client_coordinate_converts_back_to_reference_space, test_reference_rectangle_scales_edges_before_calculating_size, test_screen_and_client_coordinates_use_client_origin |
| [test_driver.py](../../../../../tools/BetterBTD.GameDriver/tests/test_driver.py) | External black-box tool implementation or test; primary symbols: test_explicit_output_path_is_resolved, test_default_output_uses_ignored_artifact_directory, test_non_png_explicit_output_is_rejected, test_evidence_completion_marker_commits_matching_pair, test_failed_overwrite_removes_old_completion_marker |
| [test_evidence.py](../../../../../tools/BetterBTD.GameDriver/tests/test_evidence.py) | External black-box tool implementation or test; primary symbols: test_valid_bundle_uses_adjacent_files_and_is_oracle_eligible, test_capture_warning_makes_evidence_ineligible_for_oracle_use, test_modified_image_is_rejected, test_missing_completion_marker_is_rejected, test_non_driver_capture_backend_is_not_oracle_eligible |
| [test_interaction.py](../../../../../tools/BetterBTD.GameDriver/tests/test_interaction.py) | External black-box tool implementation or test; primary symbols: test_changed_frame_must_then_remain_stable, test_unchanged_frames_never_complete, test_unchanged_stable_frames_are_counted_for_explicit_scroll_boundary, test_transient_change_that_returns_to_before_does_not_complete_as_changed, setUpClass |
| [test_png.py](../../../../../tools/BetterBTD.GameDriver/tests/test_png.py) | External black-box tool implementation or test; primary symbols: test_bgrx_pixels_are_encoded_as_rgb_png, test_pixel_fingerprint_includes_dimensions_and_normalized_rgb, test_frame_analysis_identifies_uniform_black_frame, test_invalid_pixel_length_is_rejected |
| [test_vision.py](../../../../../tools/BetterBTD.GameDriver/tests/test_vision.py) | External black-box tool implementation or test; primary symbols: setUpClass, test_real_holdout_frame_matches_welcome, test_real_holdout_frame_matches_modified_client_warning, test_real_holdout_frame_matches_main_menu, test_real_loading_frame_is_unknown |
| [test_visual_catalog.py](../../../../../tools/BetterBTD.GameDriver/tests/test_visual_catalog.py) | External black-box tool implementation or test; primary symbols: test_bundled_catalog_and_template_hashes_are_valid, has_detector, has_action_point, test_modal_kind_is_supported_and_other_kinds_are_rejected, test_schema_v1_legacy_catalog_remains_supported |
| [test_win32.py](../../../../../tools/BetterBTD.GameDriver/tests/test_win32.py) | External black-box tool implementation or test; primary symbols: test_foreground_request_does_not_restore_non_minimized_window, test_click_client_point_converts_to_physical_screen_coordinates, test_scroll_client_point_sends_each_wheel_detent_at_physical_point, test_scroll_rejects_invalid_direction_and_excessive_notches_before_input, test_drag_client_points_interpolates_a_physical_pixel_path |
| [visual_test_support.py](../../../../../tools/BetterBTD.GameDriver/tests/visual_test_support.py) | External black-box tool implementation or test; primary symbols: write_test_evidence |
