import unittest
from unittest.mock import patch

from betterbtd_game_driver.models import Rect, WindowSnapshot
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

    def test_click_client_point_converts_to_physical_screen_coordinates(self) -> None:
        window = WindowSnapshot(
            handle=123,
            process_id=456,
            process_name="BloonsTD6",
            title="BloonsTD6",
            visible=True,
            minimized=False,
            foreground=True,
            dpi=192,
            window_rect=Rect(600, 140, 1946, 1151),
            client_rect=Rect(620, 196, 1920, 1080),
        )
        api = WindowApi()

        with (
            patch.object(api, "snapshot", return_value=window),
            patch("betterbtd_game_driver.win32.win32api.SetCursorPos") as set_cursor,
            patch("betterbtd_game_driver.win32.win32api.mouse_event") as mouse_event,
            patch("betterbtd_game_driver.win32.time.sleep"),
        ):
            actual = api.click_client_point(123, 960, 950)

        self.assertEqual((1580, 1146), actual)
        set_cursor.assert_called_once_with((1580, 1146))
        self.assertEqual(2, mouse_event.call_count)


if __name__ == "__main__":
    unittest.main()
