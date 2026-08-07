from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

import pywintypes

from . import __version__
from .driver import DEFAULT_PROCESS_NAMES, DEFAULT_WINDOW_TITLES, CaptureRequest, GameDriver
from .errors import GameDriverError, UsageError
from .models import WindowSelector
from .win32 import enable_per_monitor_v2


class DriverArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        raise UsageError(message)


def create_parser() -> DriverArgumentParser:
    parser = DriverArgumentParser(
        prog="btd6-game-driver",
        description="Capture independent visual evidence from a real BTD6 window.",
    )
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    subparsers = parser.add_subparsers(dest="command")

    windows_parser = subparsers.add_parser(
        "windows",
        help="List matching top-level windows as JSON.",
    )
    _add_selector_arguments(windows_parser)
    windows_parser.add_argument(
        "--all",
        action="store_true",
        help="List every visible titled window instead of defaulting to BTD6 processes.",
    )

    capture_parser = subparsers.add_parser(
        "capture",
        help="Capture the physical-pixel BTD6 client area to PNG and JSON evidence.",
    )
    _add_selector_arguments(capture_parser)
    capture_parser.add_argument(
        "--output",
        type=Path,
        help="PNG output path. Defaults to artifacts/game-driver/<date>/.",
    )
    capture_parser.add_argument(
        "--launch",
        type=Path,
        help="Start this executable only when no matching BTD6 window exists.",
    )
    capture_parser.add_argument(
        "--no-activate",
        action="store_true",
        help="Do not restore or activate the target window before capture.",
    )
    capture_parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Replace an existing PNG and adjacent JSON metadata file.",
    )
    capture_parser.add_argument(
        "--settle-ms",
        type=lambda value: _bounded_integer(value, 0, 10_000, "--settle-ms"),
        default=750,
        help="Delay after activation before capture (default: 750).",
    )
    capture_parser.add_argument(
        "--activation-timeout-ms",
        type=lambda value: _bounded_integer(
            value, 100, 30_000, "--activation-timeout-ms"
        ),
        default=3_000,
        help="Foreground verification timeout (default: 3000).",
    )
    capture_parser.add_argument(
        "--window-timeout-ms",
        type=lambda value: _bounded_integer(value, 0, 120_000, "--window-timeout-ms"),
        default=3_000,
        help="Wait for an existing matching window (default: 3000).",
    )
    capture_parser.add_argument(
        "--launch-timeout-ms",
        type=lambda value: _bounded_integer(value, 100, 300_000, "--launch-timeout-ms"),
        default=60_000,
        help="Wait for a newly launched game window (default: 60000).",
    )
    return parser


def parse_args(arguments: Sequence[str]) -> argparse.Namespace:
    parser = create_parser()
    if not arguments:
        return argparse.Namespace(command="help", parser=parser)
    parsed = parser.parse_args(arguments)
    if parsed.command == "capture":
        if parsed.launch is not None and (
            parsed.window_handle is not None or parsed.process_id is not None
        ):
            raise UsageError("--launch cannot be combined with --window-handle or --process-id.")
        if parsed.output is not None and parsed.output.suffix.casefold() != ".png":
            raise UsageError("--output must name a .png file.")
    return parsed


def main(arguments: Sequence[str] | None = None) -> int:
    argv = list(sys.argv[1:] if arguments is None else arguments)
    try:
        parsed = parse_args(argv)
        if parsed.command == "help":
            parsed.parser.print_help()
            return 0

        enable_per_monitor_v2()
        driver = GameDriver()
        selector = _selector_from_args(parsed)

        if parsed.command == "windows":
            result = driver.list_windows(selector, include_all=parsed.all)
        elif parsed.command == "capture":
            result = driver.capture(
                CaptureRequest(
                    selector=selector,
                    output_path=parsed.output,
                    launch_path=parsed.launch,
                    activate=not parsed.no_activate,
                    overwrite=parsed.overwrite,
                    settle_ms=parsed.settle_ms,
                    activation_timeout_ms=parsed.activation_timeout_ms,
                    window_timeout_ms=parsed.window_timeout_ms,
                    launch_timeout_ms=parsed.launch_timeout_ms,
                )
            )
        else:
            raise UsageError(f"Unsupported command: {parsed.command}")

        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    except KeyboardInterrupt:
        _write_error("cancelled", "Operation cancelled.")
        return 130
    except GameDriverError as error:
        _write_error(error.code, error.message)
        return error.exit_code
    except pywintypes.error as error:
        _write_error("windowApiError", str(error))
        return 4
    except OSError as error:
        _write_error("operatingSystemError", str(error))
        return 1


def _add_selector_arguments(parser: argparse.ArgumentParser) -> None:
    selector_group = parser.add_mutually_exclusive_group()
    selector_group.add_argument(
        "--window-handle",
        type=_parse_window_handle,
        help="Exact top-level HWND in decimal or 0x-prefixed hexadecimal.",
    )
    selector_group.add_argument("--process-id", type=_positive_integer, help="Exact process ID.")
    selector_group.add_argument(
        "--process-name",
        help="Exact executable name, with or without .exe.",
    )
    parser.add_argument("--window-title", help="Exact, case-insensitive window title.")


def _selector_from_args(parsed: argparse.Namespace) -> WindowSelector:
    uses_default_selector = (
        parsed.window_handle is None
        and parsed.process_id is None
        and parsed.process_name is None
        and not getattr(parsed, "all", False)
    )
    process_names: tuple[str, ...]
    if parsed.process_name:
        process_names = (parsed.process_name,)
    elif uses_default_selector:
        process_names = DEFAULT_PROCESS_NAMES
    else:
        process_names = ()

    if parsed.window_title:
        titles = (parsed.window_title,)
    elif uses_default_selector:
        titles = DEFAULT_WINDOW_TITLES
    else:
        titles = ()

    return WindowSelector(
        handle=parsed.window_handle,
        process_id=parsed.process_id,
        process_names=process_names,
        titles=titles,
    )


def _parse_window_handle(value: str) -> int:
    try:
        handle = int(value, 0)
    except ValueError as error:
        raise argparse.ArgumentTypeError("window handle must be decimal or 0x-prefixed hexadecimal") from error
    if handle <= 0:
        raise argparse.ArgumentTypeError("window handle must be positive")
    return handle


def _positive_integer(value: str) -> int:
    return _bounded_integer(value, 1, 2_147_483_647, "value")


def _bounded_integer(value: str, minimum: int, maximum: int, name: str) -> int:
    try:
        parsed = int(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError(f"{name} must be an integer") from error
    if parsed < minimum or parsed > maximum:
        raise argparse.ArgumentTypeError(f"{name} must be between {minimum} and {maximum}")
    return parsed


def _write_error(code: str, message: str) -> None:
    print(
        json.dumps({"error": {"code": code, "message": message}}, ensure_ascii=False),
        file=sys.stderr,
    )
