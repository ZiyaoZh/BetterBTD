import unittest
from unittest.mock import patch

from betterbtd_game_driver.win32 import WindowApi


class WindowApiTests(unittest.TestCase):
    def test_foreground_request_does_not_restore_non_minimized_window(self) -> None:
        with (
            patch("betterbtd_game_driver.win32.win32api.GetCurrentThreadId", return_value=1),
            patch(
                "betterbtd_game_driver.win32.win32process.GetWindowThreadProcessId",
                side_effect=[(2, 100), (3, 200)],
            ),
            patch("betterbtd_game_driver.win32.win32process.AttachThreadInput"),
            patch("betterbtd_game_driver.win32.win32gui.GetForegroundWindow", return_value=456),
            patch("betterbtd_game_driver.win32.win32gui.ShowWindow") as show_window,
            patch("betterbtd_game_driver.win32.win32gui.BringWindowToTop"),
            patch("betterbtd_game_driver.win32.win32gui.SetActiveWindow"),
            patch("betterbtd_game_driver.win32.win32gui.SetFocus"),
            patch("betterbtd_game_driver.win32.win32gui.SetForegroundWindow"),
        ):
            WindowApi._request_foreground(123)

        show_window.assert_not_called()


if __name__ == "__main__":
    unittest.main()
