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
        return sorted(snapshots, key=lambda item: (item.process_name or "", item.title, item.handle))

    def snapshot(self, handle: int) -> WindowSnapshot:
        window_left, window_top, window_right, window_bottom = win32gui.GetWindowRect(handle)
        client_left, client_top, client_right, client_bottom = win32gui.GetClientRect(handle)
        screen_left, screen_top = win32gui.ClientToScreen(handle, (client_left, client_top))
        screen_right, screen_bottom = win32gui.ClientToScreen(handle, (client_right, client_bottom))
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
