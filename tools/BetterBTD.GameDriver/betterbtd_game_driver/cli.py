from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

import pywintypes

from . import __version__
from .baseline import build_templates
from .driver import DEFAULT_PROCESS_NAMES, DEFAULT_WINDOW_TITLES, CaptureRequest, GameDriver
from .evidence import read_evidence
from .errors import GameDriverError, UsageError
from .interaction import (
    ClickRequest,
    DragPointRequest,
    InteractionDriver,
    KeyPressRequest,
    PointClickRequest,
    ScrollPointRequest,
)
from .models import WindowSelector
from .navigation import (
    NavigationRequest,
    NavigationTarget,
    PageNavigator,
    load_navigation_catalog,
)
from .vision import recognize_image, write_annotation
from .visual_catalog import load_visual_catalog, visual_catalog_summary
from .win32 import (
    KEYBOARD_KEY_NAMES,
    KEYBOARD_MODIFIER_NAMES,
    MAX_DRAG_DURATION_MS,
    MAX_DRAG_STEPS,
    MAX_KEY_HOLD_MS,
    MAX_SCROLL_NOTCHES,
    MIN_DRAG_DURATION_MS,
    MIN_KEY_HOLD_MS,
    enable_per_monitor_v2,
    keyboard_chord_is_unsafe,
)


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

    click_parser = subparsers.add_parser(
        "click",
        help="Click an independently visible catalog element and observe the transition.",
    )
    _add_selector_arguments(click_parser)
    click_parser.add_argument(
        "--element",
        required=True,
        help="Stable catalog element ID to click, such as mainMenu.start.",
    )
    _add_interaction_arguments(click_parser)

    point_click_parser = subparsers.add_parser(
        "click-point",
        help="Click a 1920x1080 reference-space client point and observe the transition.",
    )
    _add_selector_arguments(point_click_parser)
    point_click_parser.add_argument(
        "--x",
        type=lambda value: _bounded_integer(value, 0, 1919, "--x"),
        required=True,
        help="Reference-space client X coordinate (0 through 1919).",
    )
    point_click_parser.add_argument(
        "--y",
        type=lambda value: _bounded_integer(value, 0, 1079, "--y"),
        required=True,
        help="Reference-space client Y coordinate (0 through 1079).",
    )
    _add_interaction_arguments(point_click_parser)

    scroll_parser = subparsers.add_parser(
        "scroll-point",
        help="Scroll at a 1920x1080 reference-space client point and observe the transition.",
    )
    _add_selector_arguments(scroll_parser)
    scroll_parser.add_argument(
        "--x",
        type=lambda value: _bounded_integer(value, 0, 1919, "--x"),
        required=True,
        help="Reference-space client X coordinate (0 through 1919).",
    )
    scroll_parser.add_argument(
        "--y",
        type=lambda value: _bounded_integer(value, 0, 1079, "--y"),
        required=True,
        help="Reference-space client Y coordinate (0 through 1079).",
    )
    scroll_parser.add_argument(
        "--direction",
        choices=("up", "down"),
        required=True,
        help="Vertical wheel direction.",
    )
    scroll_parser.add_argument(
        "--notches",
        type=lambda value: _bounded_integer(
            value,
            1,
            MAX_SCROLL_NOTCHES,
            "--notches",
        ),
        default=1,
        help="Number of wheel detents to send (default: 1).",
    )
    scroll_parser.add_argument(
        "--allow-no-change",
        action="store_true",
        help="Accept an unchanged stable frame, for example at a scroll boundary.",
    )
    _add_interaction_arguments(scroll_parser, default_change_threshold=0.005)

    drag_parser = subparsers.add_parser(
        "drag-point",
        help=(
            "Drag between two 1920x1080 reference-space client points and observe "
            "the transition."
        ),
    )
    _add_selector_arguments(drag_parser)
    for option, destination, axis, maximum in (
        ("--start-x", "start_x", "X", 1919),
        ("--start-y", "start_y", "Y", 1079),
        ("--end-x", "end_x", "X", 1919),
        ("--end-y", "end_y", "Y", 1079),
    ):
        drag_parser.add_argument(
            option,
            dest=destination,
            type=lambda value, name=option, limit=maximum: _bounded_integer(
                value, 0, limit, name
            ),
            required=True,
            help=f"Reference-space client {axis} coordinate (0 through {maximum}).",
        )
    drag_parser.add_argument(
        "--duration-ms",
        type=lambda value: _bounded_integer(
            value,
            MIN_DRAG_DURATION_MS,
            MAX_DRAG_DURATION_MS,
            "--duration-ms",
        ),
        default=500,
        help="Time from mouse-down to mouse-up in milliseconds (default: 500).",
    )
    drag_parser.add_argument(
        "--steps",
        type=lambda value: _bounded_integer(
            value,
            1,
            MAX_DRAG_STEPS,
            "--steps",
        ),
        default=10,
        help="Linear cursor movements between endpoints (default: 10).",
    )
    drag_parser.add_argument(
        "--allow-no-change",
        action="store_true",
        help="Accept an unchanged stable frame, for example at a drag boundary.",
    )
    _add_interaction_arguments(drag_parser, default_change_threshold=0.005)

    key_parser = subparsers.add_parser(
        "press-key",
        help="Press a bounded keyboard chord and observe the visual transition.",
    )
    _add_selector_arguments(key_parser)
    key_parser.add_argument(
        "--key",
        type=str.casefold,
        choices=KEYBOARD_KEY_NAMES,
        required=True,
        help="Canonical key name, such as space, escape, q, comma, or f1.",
    )
    key_parser.add_argument(
        "--modifier",
        dest="modifiers",
        action="append",
        type=str.casefold,
        choices=KEYBOARD_MODIFIER_NAMES,
        default=[],
        help="Optional ctrl, alt, or shift modifier; repeat for a chord.",
    )
    key_parser.add_argument(
        "--hold-ms",
        type=lambda value: _bounded_integer(
            value,
            MIN_KEY_HOLD_MS,
            MAX_KEY_HOLD_MS,
            "--hold-ms",
        ),
        default=50,
        help="Time between key-down and key-up in milliseconds (default: 50).",
    )
    _add_interaction_arguments(key_parser, default_change_threshold=0.005)

    navigate_parser = subparsers.add_parser(
        "navigate",
        help="Navigate between verified logical pages using the external visual Oracle.",
    )
    _add_selector_arguments(navigate_parser)
    navigate_parser.add_argument(
        "--page",
        dest="target_page",
        help="Logical target page ID, for example settings or difficultySelect.",
    )
    navigate_parser.add_argument(
        "--map",
        dest="map_id",
        help="Verified map ID; defaults the target page to difficultySelect.",
    )
    navigate_parser.add_argument(
        "--difficulty",
        dest="difficulty_id",
        choices=("easy", "medium", "hard"),
        help="Difficulty parameter used with a mode target.",
    )
    navigate_parser.add_argument(
        "--mode",
        dest="mode_id",
        help="Stable mode ID, such as standard or sandbox.",
    )
    navigate_parser.add_argument(
        "--hero",
        dest="hero_id",
        help="Stable hero ID; defaults the target page to heroSelect.",
    )
    _add_navigation_arguments(navigate_parser)

    catalog_parser = subparsers.add_parser(
        "catalog",
        help="Validate the independent visual baseline catalog and templates.",
    )
    catalog_parser.add_argument(
        "--catalog",
        type=Path,
        help="Catalog JSON path. Defaults to the bundled BTD6 visual catalog.",
    )

    recognize_parser = subparsers.add_parser(
        "recognize",
        help="Recognize a BTD6 page from an independent client screenshot.",
    )
    recognize_parser.add_argument(
        "--evidence",
        type=Path,
        required=True,
        help="Capture metadata JSON whose adjacent PNG and completion marker will be verified.",
    )
    recognize_parser.add_argument(
        "--catalog",
        type=Path,
        help="Catalog JSON path. Defaults to the bundled BTD6 visual catalog.",
    )
    recognize_parser.add_argument(
        "--annotated-output",
        type=Path,
        help="Optional PNG showing recognized element bounds for human review.",
    )
    recognize_parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Replace an existing annotated output image.",
    )

    baseline_parser = subparsers.add_parser(
        "baseline",
        help="Build deterministic templates from committed raw evidence.",
    )
    baseline_subparsers = baseline_parser.add_subparsers(dest="baseline_command")
    build_parser = baseline_subparsers.add_parser(
        "build",
        help="Rebuild every catalog template and verify its expected hash.",
    )
    build_parser.add_argument(
        "--catalog",
        type=Path,
        help="Catalog JSON path. Defaults to the bundled BTD6 visual catalog.",
    )
    build_parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Replace existing template images after all provenance checks.",
    )
    return parser


def _add_interaction_arguments(
    parser: argparse.ArgumentParser,
    *,
    default_change_threshold: float = 0.05,
) -> None:
    parser.add_argument(
        "--phase",
        required=True,
        choices=("arrange", "recover"),
        help="Input ownership phase; control is forbidden during act and assert.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        help="Directory for before/after evidence and the operation trace.",
    )
    parser.add_argument(
        "--launch",
        type=Path,
        help="Start this executable only when no matching BTD6 window exists.",
    )
    parser.add_argument(
        "--catalog",
        type=Path,
        help="Catalog JSON path. Defaults to the bundled BTD6 visual catalog.",
    )
    parser.add_argument(
        "--expect-page",
        help=(
            "Wait across stable intermediate frames and require this independently "
            "recognized final page."
        ),
    )
    parser.add_argument(
        "--expect-view-state",
        help=(
            "Wait across stable intermediate frames and require this independently "
            "recognized final catalog view state."
        ),
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Replace the command's fixed evidence and trace files.",
    )
    parser.add_argument(
        "--settle-ms",
        type=lambda value: _bounded_integer(value, 0, 10_000, "--settle-ms"),
        default=500,
        help="Delay after activation before pre-input capture (default: 500).",
    )
    parser.add_argument(
        "--activation-timeout-ms",
        type=lambda value: _bounded_integer(
            value, 100, 30_000, "--activation-timeout-ms"
        ),
        default=3_000,
        help="Foreground verification timeout (default: 3000).",
    )
    parser.add_argument(
        "--window-timeout-ms",
        type=lambda value: _bounded_integer(value, 0, 120_000, "--window-timeout-ms"),
        default=3_000,
        help="Wait for an existing matching window (default: 3000).",
    )
    parser.add_argument(
        "--launch-timeout-ms",
        type=lambda value: _bounded_integer(value, 100, 300_000, "--launch-timeout-ms"),
        default=60_000,
        help="Wait for a newly launched game window (default: 60000).",
    )
    parser.add_argument(
        "--transition-timeout-ms",
        type=lambda value: _bounded_integer(
            value, 500, 120_000, "--transition-timeout-ms"
        ),
        default=10_000,
        help=(
            "Wait for a stable frame satisfying any final page/view expectations "
            "(default: 10000)."
        ),
    )
    parser.add_argument(
        "--poll-interval-ms",
        type=lambda value: _bounded_integer(value, 50, 2_000, "--poll-interval-ms"),
        default=200,
        help="Visual transition sampling interval (default: 200).",
    )
    parser.add_argument(
        "--stable-samples",
        type=lambda value: _bounded_integer(value, 1, 20, "--stable-samples"),
        default=3,
        help="Consecutive low-difference frames required (default: 3).",
    )
    parser.add_argument(
        "--change-threshold",
        type=lambda value: _bounded_float(value, 0.000001, 1.0, "--change-threshold"),
        default=default_change_threshold,
        help=(
            "Normalized difference required from the pre-input frame "
            f"(default: {default_change_threshold})."
        ),
    )
    parser.add_argument(
        "--stability-threshold",
        type=lambda value: _bounded_float(value, 0.001, 1.0, "--stability-threshold"),
        default=0.02,
        help="Maximum normalized difference between stable frames (default: 0.02).",
    )


def _add_navigation_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--phase",
        required=True,
        choices=("arrange", "recover"),
        help="Input ownership phase; control is forbidden during act and assert.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        help="Directory for navigation steps, evidence, and navigation.json.",
    )
    parser.add_argument(
        "--launch",
        type=Path,
        help="Start this executable only when no matching BTD6 window exists.",
    )
    parser.add_argument(
        "--catalog",
        type=Path,
        help="Visual catalog JSON path. Defaults to the bundled catalog.",
    )
    parser.add_argument(
        "--navigation-catalog",
        type=Path,
        help="Page navigation JSON path. Defaults to the bundled page graph.",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Replace an existing navigation output directory.",
    )
    parser.add_argument(
        "--settle-ms",
        type=lambda value: _bounded_integer(value, 0, 10_000, "--settle-ms"),
        default=500,
    )
    parser.add_argument(
        "--activation-timeout-ms",
        type=lambda value: _bounded_integer(value, 100, 30_000, "--activation-timeout-ms"),
        default=3_000,
    )
    parser.add_argument(
        "--window-timeout-ms",
        type=lambda value: _bounded_integer(value, 0, 120_000, "--window-timeout-ms"),
        default=3_000,
    )
    parser.add_argument(
        "--launch-timeout-ms",
        type=lambda value: _bounded_integer(value, 100, 300_000, "--launch-timeout-ms"),
        default=60_000,
    )
    parser.add_argument(
        "--transition-timeout-ms",
        type=lambda value: _bounded_integer(value, 500, 120_000, "--transition-timeout-ms"),
        default=10_000,
    )
    parser.add_argument(
        "--poll-interval-ms",
        type=lambda value: _bounded_integer(value, 50, 2_000, "--poll-interval-ms"),
        default=200,
    )
    parser.add_argument(
        "--stable-samples",
        type=lambda value: _bounded_integer(value, 1, 20, "--stable-samples"),
        default=3,
    )
    parser.add_argument(
        "--change-threshold",
        type=lambda value: _bounded_float(value, 0.000001, 1.0, "--change-threshold"),
        default=0.05,
    )
    parser.add_argument(
        "--stability-threshold",
        type=lambda value: _bounded_float(value, 0.001, 1.0, "--stability-threshold"),
        default=0.02,
    )
    parser.add_argument(
        "--max-steps",
        type=lambda value: _bounded_integer(value, 1, 128, "--max-steps"),
        default=32,
    )


def parse_args(arguments: Sequence[str]) -> argparse.Namespace:
    parser = create_parser()
    if not arguments:
        return argparse.Namespace(command="help", parser=parser)
    parsed = parser.parse_args(arguments)
    if parsed.command == "baseline" and parsed.baseline_command is None:
        raise UsageError("baseline requires a subcommand such as 'build'.")
    if parsed.command in (
        "capture",
        "click",
        "click-point",
        "scroll-point",
        "drag-point",
        "press-key",
        "navigate",
    ):
        if parsed.launch is not None and (
            parsed.window_handle is not None or parsed.process_id is not None
        ):
            raise UsageError("--launch cannot be combined with --window-handle or --process-id.")
        if (
            parsed.command == "capture"
            and parsed.output is not None
            and parsed.output.suffix.casefold() != ".png"
        ):
            raise UsageError("--output must name a .png file.")
        if (
            parsed.command == "drag-point"
            and parsed.start_x == parsed.end_x
            and parsed.start_y == parsed.end_y
        ):
            raise UsageError("Drag start and end points must differ.")
        if (
            parsed.command == "press-key"
            and len(set(parsed.modifiers)) != len(parsed.modifiers)
        ):
            raise UsageError("--modifier must not contain duplicates.")
        if (
            parsed.command == "press-key"
            and keyboard_chord_is_unsafe(parsed.key, tuple(parsed.modifiers))
        ):
            raise UsageError("Reserved or system-level keyboard chords are not allowed.")
        if parsed.command == "navigate":
            _navigation_target_from_args(parsed)
    return parsed


def _navigation_target_from_args(parsed: argparse.Namespace) -> NavigationTarget:
    target_page = parsed.target_page
    difficulty_id = parsed.difficulty_id
    mode_id = parsed.mode_id
    if target_page is None:
        if parsed.hero_id is not None:
            target_page = "heroSelect"
        elif difficulty_id is not None:
            target_page = {
                "easy": "easyModeSelect",
                "medium": "mediumModeSelect",
                "hard": "hardModeSelect",
            }[difficulty_id]
            if mode_id is not None:
                target_page = "inLevel"
        elif parsed.map_id is not None:
            target_page = "difficultySelect"
        elif mode_id is not None:
            raise UsageError("--difficulty is required when --mode is used without --page.")
        else:
            target_page = "mainMenu"

    difficulty_pages = {
        "easy": "easyModeSelect",
        "medium": "mediumModeSelect",
        "hard": "hardModeSelect",
    }
    if difficulty_id is not None and target_page not in (
        difficulty_pages[difficulty_id],
        "inLevel",
    ):
        raise UsageError(
            f"--difficulty {difficulty_id} is incompatible with target page {target_page}."
        )
    if parsed.map_id is not None and target_page not in (
        "difficultySelect",
        "easyModeSelect",
        "mediumModeSelect",
        "hardModeSelect",
        "inLevel",
    ):
        raise UsageError(f"--map is incompatible with target page {target_page}.")
    if mode_id is not None and target_page not in ("inLevel", *difficulty_pages.values()):
        raise UsageError(f"--mode is incompatible with target page {target_page}.")
    if parsed.hero_id is not None and target_page not in ("heroSelect", "mainMenu"):
        raise UsageError(f"--hero is incompatible with target page {target_page}.")
    return NavigationTarget(
        page_id=target_page,
        map_id=parsed.map_id,
        difficulty_id=difficulty_id,
        mode_id=mode_id,
        hero_id=parsed.hero_id,
    )


def main(arguments: Sequence[str] | None = None) -> int:
    argv = list(sys.argv[1:] if arguments is None else arguments)
    try:
        parsed = parse_args(argv)
        if parsed.command == "help":
            parsed.parser.print_help()
            return 0

        if parsed.command == "catalog":
            result = visual_catalog_summary(load_visual_catalog(parsed.catalog))
        elif parsed.command == "recognize":
            catalog = load_visual_catalog(parsed.catalog)
            evidence = read_evidence(parsed.evidence)
            result, page_match = recognize_image(evidence, catalog)
            if parsed.annotated_output is not None:
                write_annotation(
                    evidence.image_path,
                    parsed.annotated_output,
                    page_match,
                    overwrite=parsed.overwrite,
                )
                result["annotation"] = {
                    "path": str(parsed.annotated_output.expanduser().resolve())
                }
        elif parsed.command == "baseline" and parsed.baseline_command == "build":
            catalog = load_visual_catalog(parsed.catalog, verify_templates=False)
            result = build_templates(catalog, overwrite=parsed.overwrite)
        else:
            enable_per_monitor_v2()
            driver = GameDriver()
            selector = _selector_from_args(parsed)
            if parsed.command == "navigate":
                catalog = load_visual_catalog(parsed.catalog)
                navigation = load_navigation_catalog(
                    parsed.navigation_catalog,
                    visual_catalog=catalog,
                )
                result = PageNavigator(driver, catalog, navigation).navigate(
                    NavigationRequest(
                        selector=selector,
                        target=_navigation_target_from_args(parsed),
                        phase=parsed.phase,
                        output_directory=parsed.output_dir,
                        launch_path=parsed.launch,
                        overwrite=parsed.overwrite,
                        settle_ms=parsed.settle_ms,
                        activation_timeout_ms=parsed.activation_timeout_ms,
                        window_timeout_ms=parsed.window_timeout_ms,
                        launch_timeout_ms=parsed.launch_timeout_ms,
                        transition_timeout_ms=parsed.transition_timeout_ms,
                        poll_interval_ms=parsed.poll_interval_ms,
                        stable_sample_count=parsed.stable_samples,
                        change_threshold=parsed.change_threshold,
                        stability_threshold=parsed.stability_threshold,
                        max_steps=parsed.max_steps,
                    )
                )

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
        elif parsed.command == "click":
            result = InteractionDriver(driver).click(
                ClickRequest(
                    selector=selector,
                    element_id=parsed.element,
                    phase=parsed.phase,
                    output_directory=parsed.output_dir,
                    launch_path=parsed.launch,
                    overwrite=parsed.overwrite,
                    expected_page_id=parsed.expect_page,
                    expected_view_state_id=parsed.expect_view_state,
                    settle_ms=parsed.settle_ms,
                    activation_timeout_ms=parsed.activation_timeout_ms,
                    window_timeout_ms=parsed.window_timeout_ms,
                    launch_timeout_ms=parsed.launch_timeout_ms,
                    transition_timeout_ms=parsed.transition_timeout_ms,
                    poll_interval_ms=parsed.poll_interval_ms,
                    stable_sample_count=parsed.stable_samples,
                    change_threshold=parsed.change_threshold,
                    stability_threshold=parsed.stability_threshold,
                ),
                load_visual_catalog(parsed.catalog),
            )
        elif parsed.command == "click-point":
            result = InteractionDriver(driver).click_point(
                PointClickRequest(
                    selector=selector,
                    reference_x=parsed.x,
                    reference_y=parsed.y,
                    phase=parsed.phase,
                    output_directory=parsed.output_dir,
                    launch_path=parsed.launch,
                    overwrite=parsed.overwrite,
                    expected_page_id=parsed.expect_page,
                    expected_view_state_id=parsed.expect_view_state,
                    settle_ms=parsed.settle_ms,
                    activation_timeout_ms=parsed.activation_timeout_ms,
                    window_timeout_ms=parsed.window_timeout_ms,
                    launch_timeout_ms=parsed.launch_timeout_ms,
                    transition_timeout_ms=parsed.transition_timeout_ms,
                    poll_interval_ms=parsed.poll_interval_ms,
                    stable_sample_count=parsed.stable_samples,
                    change_threshold=parsed.change_threshold,
                    stability_threshold=parsed.stability_threshold,
                ),
                load_visual_catalog(parsed.catalog),
            )
        elif parsed.command == "scroll-point":
            result = InteractionDriver(driver).scroll_point(
                ScrollPointRequest(
                    selector=selector,
                    reference_x=parsed.x,
                    reference_y=parsed.y,
                    direction=parsed.direction,
                    notches=parsed.notches,
                    allow_no_change=parsed.allow_no_change,
                    expected_view_state_id=parsed.expect_view_state,
                    phase=parsed.phase,
                    output_directory=parsed.output_dir,
                    launch_path=parsed.launch,
                    overwrite=parsed.overwrite,
                    expected_page_id=parsed.expect_page,
                    settle_ms=parsed.settle_ms,
                    activation_timeout_ms=parsed.activation_timeout_ms,
                    window_timeout_ms=parsed.window_timeout_ms,
                    launch_timeout_ms=parsed.launch_timeout_ms,
                    transition_timeout_ms=parsed.transition_timeout_ms,
                    poll_interval_ms=parsed.poll_interval_ms,
                    stable_sample_count=parsed.stable_samples,
                    change_threshold=parsed.change_threshold,
                    stability_threshold=parsed.stability_threshold,
                ),
                load_visual_catalog(parsed.catalog),
            )
        elif parsed.command == "drag-point":
            result = InteractionDriver(driver).drag_point(
                DragPointRequest(
                    selector=selector,
                    start_reference_x=parsed.start_x,
                    start_reference_y=parsed.start_y,
                    end_reference_x=parsed.end_x,
                    end_reference_y=parsed.end_y,
                    duration_ms=parsed.duration_ms,
                    steps=parsed.steps,
                    allow_no_change=parsed.allow_no_change,
                    expected_view_state_id=parsed.expect_view_state,
                    phase=parsed.phase,
                    output_directory=parsed.output_dir,
                    launch_path=parsed.launch,
                    overwrite=parsed.overwrite,
                    expected_page_id=parsed.expect_page,
                    settle_ms=parsed.settle_ms,
                    activation_timeout_ms=parsed.activation_timeout_ms,
                    window_timeout_ms=parsed.window_timeout_ms,
                    launch_timeout_ms=parsed.launch_timeout_ms,
                    transition_timeout_ms=parsed.transition_timeout_ms,
                    poll_interval_ms=parsed.poll_interval_ms,
                    stable_sample_count=parsed.stable_samples,
                    change_threshold=parsed.change_threshold,
                    stability_threshold=parsed.stability_threshold,
                ),
                load_visual_catalog(parsed.catalog),
            )
        elif parsed.command == "press-key":
            result = InteractionDriver(driver).press_key(
                KeyPressRequest(
                    selector=selector,
                    key_name=parsed.key,
                    modifiers=tuple(parsed.modifiers),
                    hold_ms=parsed.hold_ms,
                    phase=parsed.phase,
                    output_directory=parsed.output_dir,
                    launch_path=parsed.launch,
                    overwrite=parsed.overwrite,
                    expected_page_id=parsed.expect_page,
                    expected_view_state_id=parsed.expect_view_state,
                    settle_ms=parsed.settle_ms,
                    activation_timeout_ms=parsed.activation_timeout_ms,
                    window_timeout_ms=parsed.window_timeout_ms,
                    launch_timeout_ms=parsed.launch_timeout_ms,
                    transition_timeout_ms=parsed.transition_timeout_ms,
                    poll_interval_ms=parsed.poll_interval_ms,
                    stable_sample_count=parsed.stable_samples,
                    change_threshold=parsed.change_threshold,
                    stability_threshold=parsed.stability_threshold,
                ),
                load_visual_catalog(parsed.catalog),
            )
        elif parsed.command not in ("catalog", "recognize", "baseline", "navigate"):
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
        raise argparse.ArgumentTypeError(
            "window handle must be decimal or 0x-prefixed hexadecimal"
        ) from error
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


def _bounded_float(value: str, minimum: float, maximum: float, name: str) -> float:
    try:
        parsed = float(value)
    except ValueError as error:
        raise argparse.ArgumentTypeError(f"{name} must be a number") from error
    if parsed < minimum or parsed > maximum:
        raise argparse.ArgumentTypeError(f"{name} must be between {minimum} and {maximum}")
    return parsed


def _write_error(code: str, message: str) -> None:
    print(
        json.dumps({"error": {"code": code, "message": message}}, ensure_ascii=False),
        file=sys.stderr,
    )
