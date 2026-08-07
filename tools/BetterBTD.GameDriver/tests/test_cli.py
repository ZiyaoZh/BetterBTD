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
