import unittest
from unittest.mock import patch

import win32con

from betterbtd_game_driver.errors import GameDriverError
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

    def test_scroll_client_point_sends_each_wheel_detent_at_physical_point(self) -> None:
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
            actual = api.scroll_client_point(123, 960, 540, "down", 3)

        self.assertEqual((1580, 736, -360), actual)
        set_cursor.assert_called_once_with((1580, 736))
        self.assertEqual(3, mouse_event.call_count)
        for call in mouse_event.call_args_list:
            self.assertEqual((win32con.MOUSEEVENTF_WHEEL, 0, 0, -120, 0), call.args)

    def test_scroll_rejects_invalid_direction_and_excessive_notches_before_input(
        self,
    ) -> None:
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
        cases = (
            ("sideways", 1, "inputDirectionInvalid"),
            ("down", 21, "inputStepCountInvalid"),
        )

        for direction, notches, expected_code in cases:
            with (
                self.subTest(direction=direction, notches=notches),
                patch.object(api, "snapshot", return_value=window),
                patch("betterbtd_game_driver.win32.win32api.SetCursorPos") as set_cursor,
                patch("betterbtd_game_driver.win32.win32api.mouse_event") as mouse_event,
                self.assertRaises(GameDriverError) as context,
            ):
                api.scroll_client_point(123, 960, 540, direction, notches)

            self.assertEqual(expected_code, context.exception.code)
            set_cursor.assert_not_called()
            mouse_event.assert_not_called()

    def test_drag_client_points_interpolates_a_physical_pixel_path(self) -> None:
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
            patch("betterbtd_game_driver.win32.time.sleep") as sleep,
        ):
            actual = api.drag_client_points(123, 250, 850, 250, 250, 400, 4)

        self.assertEqual((870, 1046, 870, 446), actual)
        self.assertEqual(
            [
                ((870, 1046),),
                ((870, 896),),
                ((870, 746),),
                ((870, 596),),
                ((870, 446),),
            ],
            [call.args for call in set_cursor.call_args_list],
        )
        self.assertEqual(
            [win32con.MOUSEEVENTF_LEFTDOWN, win32con.MOUSEEVENTF_LEFTUP],
            [call.args[0] for call in mouse_event.call_args_list],
        )
        self.assertEqual(
            [0.05, 0.1, 0.1, 0.1, 0.1],
            [call.args[0] for call in sleep.call_args_list],
        )

    def test_drag_validates_both_points_before_sending_input(self) -> None:
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
            self.assertRaises(GameDriverError) as context,
        ):
            api.drag_client_points(123, 250, 850, 250, 1080, 400, 4)

        self.assertEqual("inputPointOutsideClient", context.exception.code)
        set_cursor.assert_not_called()
        mouse_event.assert_not_called()

    def test_drag_rejects_unsafe_duration_and_step_counts_before_input(self) -> None:
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
        cases = (
            (49, 4, "inputDurationInvalid"),
            (5_001, 4, "inputDurationInvalid"),
            (400, 0, "inputStepCountInvalid"),
            (400, 101, "inputStepCountInvalid"),
        )

        for duration_ms, steps, expected_code in cases:
            with (
                self.subTest(duration_ms=duration_ms, steps=steps),
                patch.object(api, "snapshot", return_value=window),
                patch("betterbtd_game_driver.win32.win32api.SetCursorPos") as set_cursor,
                patch("betterbtd_game_driver.win32.win32api.mouse_event") as mouse_event,
                self.assertRaises(GameDriverError) as context,
            ):
                api.drag_client_points(123, 250, 850, 250, 250, duration_ms, steps)

            self.assertEqual(expected_code, context.exception.code)
            set_cursor.assert_not_called()
            mouse_event.assert_not_called()

    def test_drag_releases_left_button_when_cursor_movement_fails(self) -> None:
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
            patch(
                "betterbtd_game_driver.win32.win32api.SetCursorPos",
                side_effect=[None, OSError("movement failed")],
            ),
            patch("betterbtd_game_driver.win32.win32api.mouse_event") as mouse_event,
            patch("betterbtd_game_driver.win32.time.sleep"),
            self.assertRaises(OSError),
        ):
            api.drag_client_points(123, 250, 850, 250, 250, 400, 4)

        self.assertEqual(
            [win32con.MOUSEEVENTF_LEFTDOWN, win32con.MOUSEEVENTF_LEFTUP],
            [call.args[0] for call in mouse_event.call_args_list],
        )

    def test_press_key_orders_chord_and_releases_in_reverse(self) -> None:
        api = WindowApi()

        with (
            patch.object(api, "is_foreground", return_value=True),
            patch("betterbtd_game_driver.win32.win32api.keybd_event") as keybd_event,
            patch("betterbtd_game_driver.win32.time.sleep") as sleep,
        ):
            actual = api.press_key(123, "left", ("shift", "ctrl"), 75)

        self.assertEqual((0x25, (0x11, 0x10)), actual)
        self.assertEqual(
            [
                (0x11, 0, 0, 0),
                (0x10, 0, 0, 0),
                (0x25, 0, win32con.KEYEVENTF_EXTENDEDKEY, 0),
                (
                    0x25,
                    0,
                    win32con.KEYEVENTF_EXTENDEDKEY | win32con.KEYEVENTF_KEYUP,
                    0,
                ),
                (0x10, 0, win32con.KEYEVENTF_KEYUP, 0),
                (0x11, 0, win32con.KEYEVENTF_KEYUP, 0),
            ],
            [call.args for call in keybd_event.call_args_list],
        )
        sleep.assert_called_once_with(0.075)

    def test_press_key_releases_pressed_modifiers_when_key_down_fails(self) -> None:
        api = WindowApi()

        with (
            patch.object(api, "is_foreground", return_value=True),
            patch(
                "betterbtd_game_driver.win32.win32api.keybd_event",
                side_effect=(None, None, OSError("key down failed"), None, None),
            ) as keybd_event,
            self.assertRaises(GameDriverError) as context,
        ):
            api.press_key(123, "q", ("ctrl", "shift"), 50)

        self.assertEqual("keyboardInputFailed", context.exception.code)
        self.assertIn("key down failed", context.exception.message)
        self.assertEqual(
            [
                (0x11, 0, 0, 0),
                (0x10, 0, 0, 0),
                (ord("Q"), 0, 0, 0),
                (0x10, 0, win32con.KEYEVENTF_KEYUP, 0),
                (0x11, 0, win32con.KEYEVENTF_KEYUP, 0),
            ],
            [call.args for call in keybd_event.call_args_list],
        )

    def test_press_key_attempts_every_release_when_key_up_fails(self) -> None:
        api = WindowApi()

        with (
            patch.object(api, "is_foreground", return_value=True),
            patch(
                "betterbtd_game_driver.win32.win32api.keybd_event",
                side_effect=(
                    None,
                    None,
                    None,
                    OSError("key up failed"),
                    None,
                    None,
                ),
            ) as keybd_event,
            patch("betterbtd_game_driver.win32.time.sleep"),
            self.assertRaises(GameDriverError) as context,
        ):
            api.press_key(123, "q", ("ctrl", "shift"), 50)

        self.assertEqual("keyboardCleanupFailed", context.exception.code)
        self.assertIn("key up failed", context.exception.message)
        self.assertEqual(6, keybd_event.call_count)
        self.assertEqual(
            [
                (ord("Q"), 0, win32con.KEYEVENTF_KEYUP, 0),
                (0x10, 0, win32con.KEYEVENTF_KEYUP, 0),
                (0x11, 0, win32con.KEYEVENTF_KEYUP, 0),
            ],
            [call.args for call in keybd_event.call_args_list[-3:]],
        )

    def test_press_key_rejects_invalid_chords_before_input(self) -> None:
        api = WindowApi()
        cases = (
            ("unsupported", (), 50, "inputKeyInvalid"),
            ("q", ("ctrl", "ctrl"), 50, "inputModifierInvalid"),
            ("q", ("windows",), 50, "inputModifierInvalid"),
            ("f10", (), 50, "inputChordUnsafe"),
            ("f4", ("alt",), 50, "inputChordUnsafe"),
            ("q", (), 9, "inputDurationInvalid"),
        )

        for key_name, modifiers, hold_ms, expected_code in cases:
            with (
                self.subTest(key=key_name, modifiers=modifiers, hold_ms=hold_ms),
                patch(
                    "betterbtd_game_driver.win32.win32api.keybd_event"
                ) as keybd_event,
                self.assertRaises(GameDriverError) as context,
            ):
                api.press_key(123, key_name, modifiers, hold_ms)

            self.assertEqual(expected_code, context.exception.code)
            keybd_event.assert_not_called()

    def test_press_key_rejects_lost_foreground_before_input(self) -> None:
        api = WindowApi()

        with (
            patch.object(api, "is_foreground", return_value=False),
            patch("betterbtd_game_driver.win32.win32api.keybd_event") as keybd_event,
            self.assertRaises(GameDriverError) as context,
        ):
            api.press_key(123, "q", (), 50)

        self.assertEqual("inputTargetNotForeground", context.exception.code)
        keybd_event.assert_not_called()

    def test_press_key_reports_input_and_cleanup_failures_together(self) -> None:
        api = WindowApi()

        with (
            patch.object(api, "is_foreground", return_value=True),
            patch(
                "betterbtd_game_driver.win32.win32api.keybd_event",
                side_effect=(None, OSError("key down failed"), OSError("ctrl up failed")),
            ) as keybd_event,
            self.assertRaises(GameDriverError) as context,
        ):
            api.press_key(123, "q", ("ctrl",), 50)

        self.assertEqual("keyboardInputAndCleanupFailed", context.exception.code)
        self.assertIn("key down failed", context.exception.message)
        self.assertIn("ctrl up failed", context.exception.message)
        self.assertEqual(3, keybd_event.call_count)


if __name__ == "__main__":
    unittest.main()
