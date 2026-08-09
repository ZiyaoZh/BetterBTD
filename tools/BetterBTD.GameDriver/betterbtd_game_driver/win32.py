from __future__ import annotations

import ctypes
import time
from ctypes import wintypes

import psutil
import pywintypes
import win32api
import win32con
import win32gui
import win32process
from mss import mss
from mss.exception import ScreenShotError

from .errors import GameDriverError
from .models import Rect, WindowSelector, WindowSnapshot


user32 = ctypes.WinDLL("user32", use_last_error=True)
dwmapi = ctypes.WinDLL("dwmapi", use_last_error=True)

user32.SetThreadDpiAwarenessContext.argtypes = [wintypes.HANDLE]
user32.SetThreadDpiAwarenessContext.restype = wintypes.HANDLE
user32.GetDpiForWindow.argtypes = [wintypes.HWND]
user32.GetDpiForWindow.restype = wintypes.UINT
dwmapi.DwmFlush.argtypes = []
dwmapi.DwmFlush.restype = ctypes.c_long

DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = ctypes.c_void_p(-4)
MAX_SCROLL_NOTCHES = 20
MIN_DRAG_DURATION_MS = 50
MAX_DRAG_DURATION_MS = 5_000
MAX_DRAG_STEPS = 100
MIN_KEY_HOLD_MS = 10
MAX_KEY_HOLD_MS = 1_000

_KEYBOARD_KEYS = {
    **{chr(code).casefold(): code for code in range(ord("A"), ord("Z") + 1)},
    **{str(number): ord(str(number)) for number in range(10)},
    **{f"f{number}": 0x6F + number for number in range(1, 13)},
    "backspace": 0x08,
    "tab": 0x09,
    "enter": 0x0D,
    "escape": 0x1B,
    "space": 0x20,
    "page-up": 0x21,
    "page-down": 0x22,
    "end": 0x23,
    "home": 0x24,
    "left": 0x25,
    "up": 0x26,
    "right": 0x27,
    "down": 0x28,
    "insert": 0x2D,
    "delete": 0x2E,
    "semicolon": 0xBA,
    "equals": 0xBB,
    "comma": 0xBC,
    "minus": 0xBD,
    "period": 0xBE,
    "slash": 0xBF,
    "grave": 0xC0,
    "left-bracket": 0xDB,
    "backslash": 0xDC,
    "right-bracket": 0xDD,
    "apostrophe": 0xDE,
}
_KEYBOARD_MODIFIERS = {
    "ctrl": 0x11,
    "alt": 0x12,
    "shift": 0x10,
}
_EXTENDED_KEY_NAMES = frozenset(
    (
        "page-up",
        "page-down",
        "end",
        "home",
        "left",
        "up",
        "right",
        "down",
        "insert",
        "delete",
    )
)
KEYBOARD_KEY_NAMES = tuple(_KEYBOARD_KEYS)
KEYBOARD_MODIFIER_NAMES = tuple(_KEYBOARD_MODIFIERS)


def keyboard_chord_is_unsafe(key_name: str, modifiers: tuple[str, ...]) -> bool:
    key = key_name.casefold()
    modifier_set = {modifier.casefold() for modifier in modifiers}
    if key == "f10":
        return True
    if "alt" in modifier_set and key in ("escape", "f4", "space", "tab"):
        return True
    if "ctrl" in modifier_set and key == "escape":
        return True
    return {"ctrl", "alt"}.issubset(modifier_set) and key == "delete"


def enable_per_monitor_v2() -> None:
    """Use physical pixels in this thread without changing system display settings."""
    ctypes.set_last_error(0)
    previous_context = user32.SetThreadDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    )
    if not previous_context:
        error_code = ctypes.get_last_error()
        message = ctypes.FormatError(error_code).strip() if error_code else "unknown error"
        raise GameDriverError(
            "dpiAwarenessFailed",
            f"Could not enable Per-Monitor V2 coordinates: Win32 error {error_code} ({message}).",
            4,
        )


class WindowApi:
    def list_windows(
        self,
        selector: WindowSelector,
        *,
        include_all: bool = False,
    ) -> list[WindowSnapshot]:
        if selector.handle is not None:
            if not win32gui.IsWindow(selector.handle):
                return []
            try:
                snapshot = self.snapshot(selector.handle)
            except (pywintypes.error, psutil.Error):
                return []
            return [snapshot] if self._matches(snapshot, selector) else []

        snapshots: list[WindowSnapshot] = []

        def callback(handle: int, _: object) -> None:
            if not win32gui.IsWindowVisible(handle):
                return
            try:
                snapshot = self.snapshot(handle)
            except (pywintypes.error, psutil.Error):
                return
            if snapshot.client_rect.area <= 0:
                return
            if include_all and not snapshot.title:
                return
            if self._matches(snapshot, selector):
                snapshots.append(snapshot)

        win32gui.EnumWindows(callback, None)
        return sorted(
            snapshots,
            key=lambda item: (item.process_name or "", item.title, item.handle),
        )

    def snapshot(self, handle: int) -> WindowSnapshot:
        window_left, window_top, window_right, window_bottom = win32gui.GetWindowRect(handle)
        client_left, client_top, client_right, client_bottom = win32gui.GetClientRect(handle)
        screen_left, screen_top = win32gui.ClientToScreen(handle, (client_left, client_top))
        screen_right, screen_bottom = win32gui.ClientToScreen(
            handle,
            (client_right, client_bottom),
        )
        _, process_id = win32process.GetWindowThreadProcessId(handle)
        dpi = int(user32.GetDpiForWindow(handle))

        return WindowSnapshot(
            handle=handle,
            process_id=process_id,
            process_name=_get_process_name(process_id),
            title=win32gui.GetWindowText(handle),
            visible=bool(win32gui.IsWindowVisible(handle)),
            minimized=bool(win32gui.IsIconic(handle)),
            foreground=win32gui.GetForegroundWindow() == handle,
            dpi=dpi if dpi > 0 else 96,
            window_rect=Rect(
                window_left,
                window_top,
                window_right - window_left,
                window_bottom - window_top,
            ),
            client_rect=Rect(
                screen_left,
                screen_top,
                screen_right - screen_left,
                screen_bottom - screen_top,
            ),
        )

    def activate(self, handle: int, timeout_seconds: float) -> bool:
        if win32gui.IsIconic(handle):
            win32gui.ShowWindow(handle, win32con.SW_RESTORE)

        deadline = time.monotonic() + timeout_seconds
        while True:
            if not win32gui.IsIconic(handle) and self.is_foreground(handle):
                return True

            self._request_foreground(handle)
            if not win32gui.IsIconic(handle) and self.is_foreground(handle):
                return True
            if time.monotonic() >= deadline:
                return False
            time.sleep(0.05)

    @staticmethod
    def is_foreground(handle: int) -> bool:
        return win32gui.GetForegroundWindow() == handle

    @staticmethod
    def flush_compositor() -> bool:
        return dwmapi.DwmFlush() == 0

    def click_client_point(
        self,
        handle: int,
        client_x: int,
        client_y: int,
    ) -> tuple[int, int]:
        snapshot = self.snapshot(handle)
        if (
            client_x < 0
            or client_y < 0
            or client_x >= snapshot.client_rect.width
            or client_y >= snapshot.client_rect.height
        ):
            raise GameDriverError(
                "inputPointOutsideClient",
                f"Client input point ({client_x}, {client_y}) is outside the game client.",
                5,
            )

        screen_x = snapshot.client_rect.x + client_x
        screen_y = snapshot.client_rect.y + client_y
        win32api.SetCursorPos((screen_x, screen_y))
        time.sleep(0.05)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        time.sleep(0.05)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        return screen_x, screen_y

    def scroll_client_point(
        self,
        handle: int,
        client_x: int,
        client_y: int,
        direction: str,
        notches: int,
    ) -> tuple[int, int, int]:
        snapshot = self.snapshot(handle)
        if (
            client_x < 0
            or client_y < 0
            or client_x >= snapshot.client_rect.width
            or client_y >= snapshot.client_rect.height
        ):
            raise GameDriverError(
                "inputPointOutsideClient",
                f"Client input point ({client_x}, {client_y}) is outside the game client.",
                5,
            )
        if direction not in ("up", "down"):
            raise GameDriverError(
                "inputDirectionInvalid",
                "Scroll direction must be up or down.",
                5,
            )
        if not 1 <= notches <= MAX_SCROLL_NOTCHES:
            raise GameDriverError(
                "inputStepCountInvalid",
                f"Scroll notches must be between 1 and {MAX_SCROLL_NOTCHES}.",
                5,
            )

        screen_x = snapshot.client_rect.x + client_x
        screen_y = snapshot.client_rect.y + client_y
        wheel_delta = win32con.WHEEL_DELTA if direction == "up" else -win32con.WHEEL_DELTA
        win32api.SetCursorPos((screen_x, screen_y))
        time.sleep(0.05)
        for index in range(notches):
            win32api.mouse_event(win32con.MOUSEEVENTF_WHEEL, 0, 0, wheel_delta, 0)
            if index + 1 < notches:
                time.sleep(0.05)
        return screen_x, screen_y, wheel_delta * notches

    def drag_client_points(
        self,
        handle: int,
        start_client_x: int,
        start_client_y: int,
        end_client_x: int,
        end_client_y: int,
        duration_ms: int,
        steps: int,
    ) -> tuple[int, int, int, int]:
        snapshot = self.snapshot(handle)
        for label, client_x, client_y in (
            ("start", start_client_x, start_client_y),
            ("end", end_client_x, end_client_y),
        ):
            if (
                client_x < 0
                or client_y < 0
                or client_x >= snapshot.client_rect.width
                or client_y >= snapshot.client_rect.height
            ):
                raise GameDriverError(
                    "inputPointOutsideClient",
                    f"Drag {label} point ({client_x}, {client_y}) is outside the game client.",
                    5,
                )
        if not MIN_DRAG_DURATION_MS <= duration_ms <= MAX_DRAG_DURATION_MS:
            raise GameDriverError(
                "inputDurationInvalid",
                f"Drag duration must be between {MIN_DRAG_DURATION_MS} and "
                f"{MAX_DRAG_DURATION_MS} milliseconds.",
                5,
            )
        if not 1 <= steps <= MAX_DRAG_STEPS:
            raise GameDriverError(
                "inputStepCountInvalid",
                f"Drag steps must be between 1 and {MAX_DRAG_STEPS}.",
                5,
            )

        start_screen_x = snapshot.client_rect.x + start_client_x
        start_screen_y = snapshot.client_rect.y + start_client_y
        end_screen_x = snapshot.client_rect.x + end_client_x
        end_screen_y = snapshot.client_rect.y + end_client_y
        win32api.SetCursorPos((start_screen_x, start_screen_y))
        time.sleep(0.05)
        win32api.mouse_event(win32con.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        try:
            step_delay_seconds = duration_ms / steps / 1000
            for index in range(1, steps + 1):
                progress = index / steps
                screen_x = round(
                    start_screen_x + (end_screen_x - start_screen_x) * progress
                )
                screen_y = round(
                    start_screen_y + (end_screen_y - start_screen_y) * progress
                )
                win32api.SetCursorPos((screen_x, screen_y))
                time.sleep(step_delay_seconds)
        finally:
            win32api.mouse_event(win32con.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
        return start_screen_x, start_screen_y, end_screen_x, end_screen_y

    def press_key(
        self,
        handle: int,
        key_name: str,
        modifiers: tuple[str, ...],
        hold_ms: int,
    ) -> tuple[int, tuple[int, ...]]:
        normalized_key = key_name.casefold()
        normalized_modifiers = tuple(modifier.casefold() for modifier in modifiers)
        if normalized_key not in _KEYBOARD_KEYS:
            raise GameDriverError(
                "inputKeyInvalid",
                f"Unsupported keyboard key: {key_name}",
                5,
            )
        if len(set(normalized_modifiers)) != len(normalized_modifiers):
            raise GameDriverError(
                "inputModifierInvalid",
                "Keyboard modifiers must not contain duplicates.",
                5,
            )
        if any(modifier not in _KEYBOARD_MODIFIERS for modifier in normalized_modifiers):
            raise GameDriverError(
                "inputModifierInvalid",
                "Keyboard modifiers must be ctrl, alt, or shift.",
                5,
            )
        if keyboard_chord_is_unsafe(normalized_key, normalized_modifiers):
            raise GameDriverError(
                "inputChordUnsafe",
                "Reserved or system-level keyboard chords are not allowed.",
                5,
            )
        if not MIN_KEY_HOLD_MS <= hold_ms <= MAX_KEY_HOLD_MS:
            raise GameDriverError(
                "inputDurationInvalid",
                f"Key hold duration must be between {MIN_KEY_HOLD_MS} and "
                f"{MAX_KEY_HOLD_MS} milliseconds.",
                5,
            )

        ordered_modifiers = tuple(
            modifier
            for modifier in KEYBOARD_MODIFIER_NAMES
            if modifier in normalized_modifiers
        )
        modifier_virtual_keys = tuple(
            _KEYBOARD_MODIFIERS[modifier] for modifier in ordered_modifiers
        )
        pressed: list[tuple[int, bool]] = []
        input_error: Exception | None = None
        release_errors: list[Exception] = []
        try:
            for virtual_key in modifier_virtual_keys:
                self._require_keyboard_target_foreground(handle)
                _send_keyboard_event(virtual_key, extended=False, key_up=False)
                pressed.append((virtual_key, False))

            key_virtual_key = _KEYBOARD_KEYS[normalized_key]
            key_extended = normalized_key in _EXTENDED_KEY_NAMES
            self._require_keyboard_target_foreground(handle)
            _send_keyboard_event(key_virtual_key, extended=key_extended, key_up=False)
            pressed.append((key_virtual_key, key_extended))
            time.sleep(hold_ms / 1000)
        except Exception as error:
            input_error = error
        finally:
            # Attempt every release so one Win32 failure does not strand other keys.
            for virtual_key, extended in reversed(pressed):
                try:
                    _send_keyboard_event(virtual_key, extended=extended, key_up=True)
                except Exception as error:
                    release_errors.append(error)

        if input_error is not None and release_errors:
            raise GameDriverError(
                "keyboardInputAndCleanupFailed",
                f"Keyboard input failed ({_error_description(input_error)}); key release "
                f"also failed ({_error_descriptions(release_errors)}).",
                5,
            ) from input_error
        if input_error is not None:
            if isinstance(input_error, GameDriverError):
                raise input_error
            raise GameDriverError(
                "keyboardInputFailed",
                f"Keyboard input failed: {_error_description(input_error)}.",
                5,
            ) from input_error
        if release_errors:
            raise GameDriverError(
                "keyboardCleanupFailed",
                f"Keyboard key release failed: {_error_descriptions(release_errors)}.",
                5,
            ) from release_errors[0]

        return _KEYBOARD_KEYS[normalized_key], modifier_virtual_keys

    def _require_keyboard_target_foreground(self, handle: int) -> None:
        if not self.is_foreground(handle):
            raise GameDriverError(
                "inputTargetNotForeground",
                "The BTD6 window lost foreground ownership; remaining keyboard input "
                "was not sent.",
                5,
            )

    @staticmethod
    def virtual_screen_rect() -> Rect:
        return Rect(
            win32api.GetSystemMetrics(win32con.SM_XVIRTUALSCREEN),
            win32api.GetSystemMetrics(win32con.SM_YVIRTUALSCREEN),
            win32api.GetSystemMetrics(win32con.SM_CXVIRTUALSCREEN),
            win32api.GetSystemMetrics(win32con.SM_CYVIRTUALSCREEN),
        )

    @staticmethod
    def _matches(snapshot: WindowSnapshot, selector: WindowSelector) -> bool:
        if selector.process_id is not None and snapshot.process_id != selector.process_id:
            return False
        if selector.process_names:
            actual_name = _normalize_process_name(snapshot.process_name or "")
            expected_names = {_normalize_process_name(name) for name in selector.process_names}
            if actual_name not in expected_names:
                return False
        if selector.titles:
            expected_titles = {title.casefold() for title in selector.titles}
            if snapshot.title.casefold() not in expected_titles:
                return False
        return True

    @staticmethod
    def _request_foreground(handle: int) -> None:
        current_thread_id = win32api.GetCurrentThreadId()
        target_thread_id, _ = win32process.GetWindowThreadProcessId(handle)
        foreground_handle = win32gui.GetForegroundWindow()
        foreground_thread_id = (
            win32process.GetWindowThreadProcessId(foreground_handle)[0]
            if foreground_handle
            else 0
        )

        attached_target = False
        attached_foreground = False
        try:
            if target_thread_id and target_thread_id != current_thread_id:
                try:
                    win32process.AttachThreadInput(current_thread_id, target_thread_id, True)
                    attached_target = True
                except pywintypes.error:
                    pass
            if (
                foreground_thread_id
                and foreground_thread_id != current_thread_id
                and foreground_thread_id != target_thread_id
            ):
                try:
                    win32process.AttachThreadInput(current_thread_id, foreground_thread_id, True)
                    attached_foreground = True
                except pywintypes.error:
                    pass

            win32gui.BringWindowToTop(handle)
            try:
                win32gui.SetActiveWindow(handle)
                win32gui.SetFocus(handle)
            except pywintypes.error:
                pass
            try:
                win32gui.SetForegroundWindow(handle)
            except pywintypes.error:
                pass
        finally:
            if attached_foreground:
                win32process.AttachThreadInput(current_thread_id, foreground_thread_id, False)
            if attached_target:
                win32process.AttachThreadInput(current_thread_id, target_thread_id, False)


def capture_desktop_rect(rect: Rect) -> bytes:
    if rect.width <= 0 or rect.height <= 0:
        raise GameDriverError("invalidClientRect", "The game client rectangle is empty.", 5)

    try:
        with mss(backend="gdi", with_cursor=False) as capturer:
            screenshot = capturer.grab(
                {
                    "left": rect.x,
                    "top": rect.y,
                    "width": rect.width,
                    "height": rect.height,
                }
            )
            return bytes(screenshot.bgra)
    except ScreenShotError as error:
        raise GameDriverError("captureFailed", f"Desktop capture failed: {error}", 5) from error


def _send_keyboard_event(virtual_key: int, *, extended: bool, key_up: bool) -> None:
    flags = win32con.KEYEVENTF_EXTENDEDKEY if extended else 0
    if key_up:
        flags |= win32con.KEYEVENTF_KEYUP
    win32api.keybd_event(virtual_key, 0, flags, 0)


def _error_description(error: Exception) -> str:
    if isinstance(error, GameDriverError):
        return f"{error.code}: {error.message}"
    return f"{type(error).__name__}: {error}"


def _error_descriptions(errors: list[Exception]) -> str:
    return "; ".join(_error_description(error) for error in errors)


def _get_process_name(process_id: int) -> str | None:
    try:
        process_name = psutil.Process(process_id).name()
    except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
        return None
    return process_name[:-4] if process_name.casefold().endswith(".exe") else process_name


def _normalize_process_name(process_name: str) -> str:
    normalized = process_name.strip()
    if normalized.casefold().endswith(".exe"):
        normalized = normalized[:-4]
    return normalized.casefold()
