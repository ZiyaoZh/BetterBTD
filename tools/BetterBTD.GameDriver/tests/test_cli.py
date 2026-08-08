from pathlib import Path
from contextlib import redirect_stdout
from io import StringIO
import unittest
from unittest.mock import patch

from betterbtd_game_driver.cli import _selector_from_args, main, parse_args
from betterbtd_game_driver.driver import DEFAULT_PROCESS_NAMES, DEFAULT_WINDOW_TITLES
from betterbtd_game_driver.errors import UsageError


class CommandLineTests(unittest.TestCase):
    def test_no_arguments_selects_help(self) -> None:
        parsed = parse_args([])

        self.assertEqual("help", parsed.command)

    def test_capture_defaults_to_real_btd6_window_titles_and_processes(self) -> None:
        parsed = parse_args(["capture"])
        selector = _selector_from_args(parsed)

        self.assertEqual(DEFAULT_PROCESS_NAMES, selector.process_names)
        self.assertEqual(DEFAULT_WINDOW_TITLES, selector.titles)
        self.assertFalse(parsed.no_activate)
        self.assertEqual(750, parsed.settle_ms)

    def test_explicit_process_name_does_not_apply_default_title_filter(self) -> None:
        parsed = parse_args(["windows", "--process-name", "CustomGame.exe"])
        selector = _selector_from_args(parsed)

        self.assertEqual(("CustomGame.exe",), selector.process_names)
        self.assertEqual((), selector.titles)

    def test_hexadecimal_window_handle_is_supported(self) -> None:
        parsed = parse_args(["capture", "--window-handle", "0x1234"])
        selector = _selector_from_args(parsed)

        self.assertEqual(0x1234, selector.handle)
        self.assertEqual((), selector.process_names)

    def test_launch_cannot_be_combined_with_exact_handle(self) -> None:
        with self.assertRaisesRegex(UsageError, "--launch cannot be combined"):
            parse_args(["capture", "--window-handle", "0x1234", "--launch", "game.exe"])

    def test_output_must_be_png(self) -> None:
        with self.assertRaisesRegex(UsageError, "must name a .png"):
            parse_args(["capture", "--output", str(Path("capture.jpg"))])

    def test_recognize_requires_committed_evidence_metadata(self) -> None:
        parsed = parse_args(["recognize", "--evidence", "frame.json"])

        self.assertEqual("recognize", parsed.command)
        self.assertEqual(Path("frame.json"), parsed.evidence)

    def test_click_requires_element_and_uses_transition_defaults(self) -> None:
        parsed = parse_args(
            ["click", "--element", "mainMenu.start", "--phase", "arrange"]
        )
        selector = _selector_from_args(parsed)

        self.assertEqual("mainMenu.start", parsed.element)
        self.assertEqual(DEFAULT_PROCESS_NAMES, selector.process_names)
        self.assertEqual(10_000, parsed.transition_timeout_ms)
        self.assertEqual(3, parsed.stable_samples)
        self.assertEqual(0.05, parsed.change_threshold)
        self.assertEqual(0.02, parsed.stability_threshold)
        self.assertIsNone(parsed.expect_view_state)

    def test_click_launch_cannot_be_combined_with_exact_process(self) -> None:
        with self.assertRaisesRegex(UsageError, "--launch cannot be combined"):
            parse_args(
                [
                    "click",
                    "--element",
                    "mainMenu.start",
                    "--phase",
                    "arrange",
                    "--process-id",
                    "123",
                    "--launch",
                    "game.exe",
                ]
            )

    def test_click_rejects_act_phase(self) -> None:
        with self.assertRaisesRegex(UsageError, "invalid choice"):
            parse_args(
                ["click", "--element", "mainMenu.start", "--phase", "act"]
            )

    def test_click_and_click_point_accept_expected_view_state(self) -> None:
        cases = (
            ["click", "--element", "mainMenu.start"],
            ["click-point", "--x", "960", "--y", "540"],
        )

        for arguments in cases:
            with self.subTest(command=arguments[0]):
                parsed = parse_args(
                    [
                        *arguments,
                        "--phase",
                        "arrange",
                        "--expect-view-state",
                        "heroSelect.bottom",
                    ]
                )

                self.assertEqual("heroSelect.bottom", parsed.expect_view_state)

    def test_click_point_uses_reference_coordinates_and_transition_defaults(self) -> None:
        parsed = parse_args(
            [
                "click-point",
                "--x",
                "960",
                "--y",
                "540",
                "--phase",
                "arrange",
                "--change-threshold",
                "0.00005",
            ]
        )
        selector = _selector_from_args(parsed)

        self.assertEqual((960, 540), (parsed.x, parsed.y))
        self.assertEqual(DEFAULT_PROCESS_NAMES, selector.process_names)
        self.assertEqual(10_000, parsed.transition_timeout_ms)
        self.assertEqual(3, parsed.stable_samples)
        self.assertEqual(0.00005, parsed.change_threshold)

    def test_click_point_rejects_coordinates_outside_reference_client(self) -> None:
        with self.assertRaisesRegex(UsageError, "--x must be between 0 and 1919"):
            parse_args(
                ["click-point", "--x", "1920", "--y", "540", "--phase", "arrange"]
            )

    def test_click_point_rejects_act_phase(self) -> None:
        with self.assertRaisesRegex(UsageError, "invalid choice"):
            parse_args(
                ["click-point", "--x", "960", "--y", "540", "--phase", "act"]
            )

    def test_scroll_uses_reference_coordinates_and_transition_defaults(self) -> None:
        parsed = parse_args(
            [
                "scroll-point",
                "--x",
                "960",
                "--y",
                "540",
                "--direction",
                "down",
                "--notches",
                "3",
                "--phase",
                "arrange",
            ]
        )
        selector = _selector_from_args(parsed)

        self.assertEqual((960, 540), (parsed.x, parsed.y))
        self.assertEqual(("down", 3), (parsed.direction, parsed.notches))
        self.assertEqual(DEFAULT_PROCESS_NAMES, selector.process_names)
        self.assertEqual(10_000, parsed.transition_timeout_ms)
        self.assertEqual(0.005, parsed.change_threshold)

    def test_scroll_rejects_act_phase(self) -> None:
        with self.assertRaisesRegex(UsageError, "invalid choice"):
            parse_args(
                [
                    "scroll-point",
                    "--x",
                    "960",
                    "--y",
                    "540",
                    "--direction",
                    "down",
                    "--phase",
                    "act",
                ]
            )

    def test_scroll_rejects_zero_notches(self) -> None:
        with self.assertRaisesRegex(UsageError, "--notches must be between 1 and 20"):
            parse_args(
                [
                    "scroll-point",
                    "--x",
                    "960",
                    "--y",
                    "540",
                    "--direction",
                    "down",
                    "--notches",
                    "0",
                    "--phase",
                    "arrange",
                ]
            )

    def test_scroll_has_lower_change_threshold_and_opt_in_no_change(self) -> None:
        parsed = parse_args(
            [
                "scroll-point",
                "--x",
                "960",
                "--y",
                "540",
                "--direction",
                "up",
                "--allow-no-change",
                "--expect-view-state",
                "extras.top",
                "--phase",
                "recover",
            ]
        )

        self.assertEqual(0.005, parsed.change_threshold)
        self.assertTrue(parsed.allow_no_change)
        self.assertEqual("extras.top", parsed.expect_view_state)

    def test_drag_uses_two_reference_points_and_deterministic_defaults(self) -> None:
        parsed = parse_args(
            [
                "drag-point",
                "--start-x",
                "250",
                "--start-y",
                "850",
                "--end-x",
                "250",
                "--end-y",
                "250",
                "--phase",
                "arrange",
                "--expect-page",
                "heroSelect",
            ]
        )

        self.assertEqual((250, 850), (parsed.start_x, parsed.start_y))
        self.assertEqual((250, 250), (parsed.end_x, parsed.end_y))
        self.assertEqual(500, parsed.duration_ms)
        self.assertEqual(10, parsed.steps)
        self.assertEqual(0.005, parsed.change_threshold)
        self.assertFalse(parsed.allow_no_change)

    def test_drag_rejects_act_phase(self) -> None:
        with self.assertRaisesRegex(UsageError, "invalid choice"):
            parse_args(
                [
                    "drag-point",
                    "--start-x",
                    "250",
                    "--start-y",
                    "850",
                    "--end-x",
                    "250",
                    "--end-y",
                    "250",
                    "--phase",
                    "act",
                ]
            )

    def test_drag_rejects_identical_points(self) -> None:
        with self.assertRaisesRegex(UsageError, "must differ"):
            parse_args(
                [
                    "drag-point",
                    "--start-x",
                    "250",
                    "--start-y",
                    "850",
                    "--end-x",
                    "250",
                    "--end-y",
                    "850",
                    "--phase",
                    "recover",
                ]
            )

    def test_drag_rejects_endpoint_outside_reference_client(self) -> None:
        with self.assertRaisesRegex(UsageError, "--end-y must be between 0 and 1079"):
            parse_args(
                [
                    "drag-point",
                    "--start-x",
                    "250",
                    "--start-y",
                    "850",
                    "--end-x",
                    "250",
                    "--end-y",
                    "1080",
                    "--phase",
                    "recover",
                ]
            )

    def test_baseline_requires_subcommand(self) -> None:
        with self.assertRaisesRegex(UsageError, "requires a subcommand"):
            parse_args(["baseline"])

    def test_catalog_command_does_not_initialize_win32(self) -> None:
        with patch("betterbtd_game_driver.cli.enable_per_monitor_v2") as enable_dpi:
            with redirect_stdout(StringIO()):
                exit_code = main(["catalog"])

        self.assertEqual(0, exit_code)
        enable_dpi.assert_not_called()


if __name__ == "__main__":
    unittest.main()
