from __future__ import annotations

import json
import os
import re
import uuid
from collections import deque
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Protocol

from .driver import CaptureRequest, GameDriver
from .evidence import read_evidence
from .errors import GameDriverError, UsageError
from .interaction import (
    ClickRequest,
    InteractionDriver,
    ScrollPointRequest,
    resolve_interaction_output_directory,
)
from .models import WindowSelector
from .vision import recognize_image
from .visual_catalog import VisualCatalog, VisualElement


DEFAULT_NAVIGATION_PATH = (
    Path(__file__).resolve().parent.parent / "navigation" / "page-navigation.json"
)
_MAP_VIEW_STATE_PATTERN = re.compile(r"^mapSelect\.(?P<category>[a-z]+)(?P<page>[0-9]{2})$")


@dataclass(frozen=True, slots=True)
class NavigationEdge:
    source_page: str
    action_method: str
    allowed_target_pages: tuple[str, ...]
    element: str | None
    element_template: str | None
    parameter_names: tuple[str, ...]
    allowed_parameters: dict[str, tuple[str, ...]]
    conditions: dict[str, str]
    settle_rule: str
    max_attempts: int
    side_effects: tuple[str, ...]
    evidence: tuple[dict[str, str], ...]

    def resolve_element(self, target: NavigationTarget) -> str:
        if self.element is not None:
            return self.element
        if self.element_template is None:
            raise GameDriverError(
                "navigationCatalogInvalid",
                f"Navigation edge {self.action_method} has no action element.",
                3,
            )
        values = {
            "mapId": target.map_id,
            "heroId": target.hero_id,
        }
        missing = [
            name
            for name in self.parameter_names
            if not isinstance(values.get(name), str) or not values[name]
        ]
        if missing:
            raise UsageError(
                f"Navigation action {self.action_method} requires parameters: "
                f"{', '.join(missing)}."
            )
        for name, allowed_values in self.allowed_parameters.items():
            actual_value = values.get(name)
            if actual_value not in allowed_values:
                raise UsageError(
                    f"Parameter {name}={actual_value} is not allowed for "
                    f"navigation action {self.action_method}."
                )
        try:
            return self.element_template.format(**values)
        except KeyError as error:
            raise GameDriverError(
                "navigationCatalogInvalid",
                f"Navigation edge {self.action_method} uses an unknown template "
                f"parameter: {error.args[0]}",
                3,
            ) from error


@dataclass(frozen=True, slots=True)
class NavigationCatalog:
    path: Path
    catalog_id: str
    catalog_version: int
    pages: tuple[str, ...]
    edges: tuple[NavigationEdge, ...]
    map_targets: dict[str, str]

    def find_route(
        self,
        source_page: str,
        target_page: str,
        target: NavigationTarget | None = None,
    ) -> tuple[NavigationEdge, ...]:
        if source_page == target_page:
            return ()
        edges_by_source: dict[str, list[NavigationEdge]] = {}
        for edge in self.edges:
            edges_by_source.setdefault(edge.source_page, []).append(edge)

        queue: deque[tuple[str, tuple[NavigationEdge, ...]]] = deque(
            [(source_page, ())]
        )
        visited = {source_page}
        while queue:
            page, route = queue.popleft()
            for edge in edges_by_source.get(page, ()):
                if target is not None and not _edge_matches_target(edge, target):
                    continue
                for next_page in edge.allowed_target_pages:
                    next_route = (*route, edge)
                    if next_page == target_page:
                        return next_route
                    if next_page not in visited:
                        visited.add(next_page)
                        queue.append((next_page, next_route))
        raise GameDriverError(
            "navigationRouteNotFound",
            f"No verified page route exists from {source_page} to {target_page}.",
            6,
        )

    def edge_for(self, source_page: str, action_method: str) -> NavigationEdge:
        matches = tuple(
            edge
            for edge in self.edges
            if edge.source_page == source_page and edge.action_method == action_method
        )
        if len(matches) != 1:
            raise GameDriverError(
                "navigationCatalogInvalid",
                f"Expected exactly one {action_method} edge from {source_page}; "
                f"found {len(matches)}.",
                3,
            )
        return matches[0]


@dataclass(frozen=True, slots=True)
class NavigationTarget:
    page_id: str
    map_id: str | None = None
    difficulty_id: str | None = None
    mode_id: str | None = None
    hero_id: str | None = None


@dataclass(frozen=True, slots=True)
class NavigationRequest:
    selector: WindowSelector
    target: NavigationTarget
    phase: str
    output_directory: Path | None
    launch_path: Path | None
    overwrite: bool
    settle_ms: int = 500
    activation_timeout_ms: int = 3_000
    window_timeout_ms: int = 3_000
    launch_timeout_ms: int = 60_000
    transition_timeout_ms: int = 10_000
    poll_interval_ms: int = 200
    stable_sample_count: int = 3
    change_threshold: float = 0.05
    stability_threshold: float = 0.02
    max_steps: int = 32


@dataclass(frozen=True, slots=True)
class NavigationObservation:
    status: str
    page_id: str | None
    view_state_id: str | None
    oracle_eligible: bool
    document: dict[str, object]

    @property
    def is_safe(self) -> bool:
        return (
            self.status == "matched"
            and self.page_id is not None
            and self.oracle_eligible
        )


@dataclass(frozen=True, slots=True)
class PagePreparation:
    observation: NavigationObservation
    completed: bool


class NavigationActionRunner(Protocol):
    def click(
        self,
        element_id: str,
        *,
        expected_page_id: str | None = None,
        expected_view_state_id: str | None = None,
    ) -> NavigationObservation:
        ...

    def scroll(
        self,
        reference_x: int,
        reference_y: int,
        direction: str,
        *,
        expected_page_id: str | None = None,
        expected_view_state_id: str | None = None,
    ) -> NavigationObservation:
        ...


class PageObject(Protocol):
    page_id: str

    def prepare(
        self,
        target: NavigationTarget,
        observation: NavigationObservation,
        runner: NavigationActionRunner,
    ) -> PagePreparation:
        ...

    def leave(
        self,
        edge: NavigationEdge,
        target: NavigationTarget,
        runner: NavigationActionRunner,
    ) -> NavigationObservation:
        ...


class CatalogPageObject:
    def __init__(self, page_id: str, catalog: VisualCatalog) -> None:
        self.page_id = page_id
        self._catalog = catalog

    def prepare(
        self,
        target: NavigationTarget,
        observation: NavigationObservation,
        runner: NavigationActionRunner,
    ) -> PagePreparation:
        return PagePreparation(observation, True)

    def leave(
        self,
        edge: NavigationEdge,
        target: NavigationTarget,
        runner: NavigationActionRunner,
    ) -> NavigationObservation:
        return runner.click(
            edge.resolve_element(target),
            expected_page_id=None,
            expected_view_state_id=None,
        )

    def element(self, element_id: str) -> VisualElement:
        for page in self._catalog.pages:
            for element in page.elements:
                if element.id == element_id:
                    return element
        raise GameDriverError(
            "navigationElementUnknown",
            f"Navigation element does not exist in the visual catalog: {element_id}",
            3,
        )


class MapSelectPage(CatalogPageObject):
    def __init__(self, catalog: VisualCatalog, navigation: NavigationCatalog) -> None:
        super().__init__("mapSelect", catalog)
        self._navigation = navigation

    def prepare(
        self,
        target: NavigationTarget,
        observation: NavigationObservation,
        runner: NavigationActionRunner,
    ) -> PagePreparation:
        if target.map_id is None:
            return PagePreparation(observation, True)
        desired_view_state = self._navigation.map_targets.get(target.map_id)
        if desired_view_state is None:
            raise UsageError(
                f"Map {target.map_id} is not in the verified navigation catalog."
            )
        if observation.view_state_id == desired_view_state:
            return PagePreparation(observation, True)
        if observation.view_state_id is None:
            raise GameDriverError(
                "navigationViewStateRequired",
                "Map selection requires an independently recognized map view state.",
                6,
            )

        current_match = _MAP_VIEW_STATE_PATTERN.fullmatch(observation.view_state_id)
        desired_match = _MAP_VIEW_STATE_PATTERN.fullmatch(desired_view_state)
        if current_match is None or desired_match is None:
            raise GameDriverError(
                "navigationViewStateUnsupported",
                f"Map view state transition is not modeled: {observation.view_state_id} "
                f"to {desired_view_state}.",
                6,
            )
        current_category = current_match.group("category")
        desired_category = desired_match.group("category")
        if current_category != desired_category:
            category_element = f"mapSelect.{desired_category}"
            next_observation = runner.click(
                category_element,
                expected_page_id=self.page_id,
                expected_view_state_id=f"mapSelect.{desired_category}01",
            )
            observation = next_observation

        page = next(page for page in self._catalog.pages if page.id == self.page_id)
        view_states = [
            view_state.id
            for view_state in page.view_states
            if _MAP_VIEW_STATE_PATTERN.fullmatch(view_state.id) is not None
            and _MAP_VIEW_STATE_PATTERN.fullmatch(view_state.id).group("category")
            == desired_category
        ]
        try:
            current_index = view_states.index(observation.view_state_id or "")
            desired_index = view_states.index(desired_view_state)
        except ValueError as error:
            raise GameDriverError(
                "navigationViewStateUnsupported",
                f"Map view state transition is not modeled: {observation.view_state_id} "
                f"to {desired_view_state}.",
                6,
            ) from error

        while current_index != desired_index:
            direction = "nextPage" if current_index < desired_index else "previousPage"
            current_index += 1 if current_index < desired_index else -1
            observation = runner.click(
                f"mapSelect.{direction}",
                expected_page_id=self.page_id,
                expected_view_state_id=view_states[current_index],
            )
        return PagePreparation(observation, True)

    def leave(
        self,
        edge: NavigationEdge,
        target: NavigationTarget,
        runner: NavigationActionRunner,
    ) -> NavigationObservation:
        if edge.action_method != "enterMap":
            return super().leave(edge, target, runner)
        if target.map_id is None:
            raise UsageError("enterMap requires --map.")
        return runner.click(
            edge.resolve_element(target),
            expected_page_id=None,
            expected_view_state_id=None,
        )


class HeroSelectPage(CatalogPageObject):
    def __init__(self, catalog: VisualCatalog) -> None:
        super().__init__("heroSelect", catalog)

    def prepare(
        self,
        target: NavigationTarget,
        observation: NavigationObservation,
        runner: NavigationActionRunner,
    ) -> PagePreparation:
        if target.hero_id is None:
            return PagePreparation(observation, True)
        element_id = f"heroSelect.{target.hero_id}"
        element = self.element(element_id)
        placement_states = tuple(placement.view_state_id for placement in element.placements)
        if not placement_states:
            raise GameDriverError(
                "navigationElementUnsupported",
                f"Hero element has no independently visible placement: {element_id}",
                6,
            )
        if observation.view_state_id not in placement_states:
            desired_state = placement_states[-1]
            direction = "down" if desired_state.endswith("bottom") else "up"
            observation = runner.scroll(
                250,
                600,
                direction,
                expected_page_id=self.page_id,
                expected_view_state_id=desired_state,
            )
        observation = runner.click(element_id, expected_page_id=self.page_id)
        return PagePreparation(observation, True)


def load_navigation_catalog(
    path: Path | None = None,
    *,
    visual_catalog: VisualCatalog,
) -> NavigationCatalog:
    navigation_path = (path or DEFAULT_NAVIGATION_PATH).expanduser().resolve()
    try:
        document = json.loads(navigation_path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise GameDriverError(
            "navigationCatalogNotFound",
            f"Navigation catalog does not exist: {navigation_path}",
            3,
        ) from error
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise GameDriverError(
            "navigationCatalogInvalid",
            f"Navigation catalog could not be read: {navigation_path}: {error}",
            3,
        ) from error

    try:
        return _parse_navigation_catalog(document, navigation_path, visual_catalog)
    except (KeyError, TypeError, ValueError) as error:
        raise GameDriverError(
            "navigationCatalogInvalid",
            f"Navigation catalog is invalid: {navigation_path}: {error}",
            3,
        ) from error


class PageNavigator:
    def __init__(
        self,
        game_driver: GameDriver,
        catalog: VisualCatalog,
        navigation: NavigationCatalog,
    ) -> None:
        self._game_driver = game_driver
        self._catalog = catalog
        self._navigation = navigation
        self._interaction = InteractionDriver(game_driver)
        self._page_objects: dict[str, PageObject] = {
            page_id: CatalogPageObject(page_id, catalog)
            for page_id in navigation.pages
        }
        self._page_objects["mapSelect"] = MapSelectPage(catalog, navigation)
        self._page_objects["heroSelect"] = HeroSelectPage(catalog)

    def navigate(self, request: NavigationRequest) -> dict[str, object]:
        if request.phase not in ("arrange", "recover"):
            raise UsageError("Input phase must be arrange or recover.")
        if request.target.page_id not in self._navigation.pages:
            raise UsageError(f"Unknown navigation target page: {request.target.page_id}")
        if request.target.map_id is not None and request.target.map_id not in self._navigation.map_targets:
            raise UsageError(
                f"Map {request.target.map_id} is not in the verified navigation catalog."
            )

        output_directory = resolve_interaction_output_directory(
            request.output_directory,
            request.target.page_id,
            _utc_now(),
            operation_prefix="navigate",
        )
        _validate_navigation_outputs(output_directory, request.overwrite)
        trace: dict[str, object] = {
            "schemaVersion": 1,
            "operationRole": "independentNavigationTrace",
            "source": "BetterBTD.GameDriver",
            "operation": "navigate",
            "inputOwnershipPhase": request.phase,
            "target": _target_to_dict(request.target),
            "navigationCatalog": {
                "path": str(self._navigation.path),
                "id": self._navigation.catalog_id,
                "version": self._navigation.catalog_version,
            },
            "status": "pending",
            "steps": [],
        }
        trace_path = output_directory / "navigation.json"
        map_parameter_prepared = request.target.map_id is None
        hero_parameter_prepared = request.target.hero_id is None
        try:
            current = self._capture_initial(request, output_directory)
            trace["initial"] = _observation_to_dict(current)
            runner = _NavigationActionRunner(
                self,
                request,
                output_directory,
                trace,
                self._interaction,
                self._catalog,
            )
            for _ in range(request.max_steps):
                page_object = self._page_objects.get(current.page_id or "")
                if page_object is None:
                    raise GameDriverError(
                        "navigationPageUnsupported",
                        f"No page object is registered for {current.page_id}.",
                        6,
                    )
                preparation = page_object.prepare(request.target, current, runner)
                current = preparation.observation
                _require_safe_observation(current, "page preparation")
                if (
                    request.target.map_id is not None
                    and current.page_id == "mapSelect"
                    and preparation.completed
                ):
                    map_parameter_prepared = True
                if request.target.hero_id is not None and current.page_id == "heroSelect" and preparation.completed:
                    hero_parameter_prepared = True
                if (
                    current.page_id == request.target.page_id
                    and preparation.completed
                    and map_parameter_prepared
                    and hero_parameter_prepared
                ):
                    trace["status"] = "completed"
                    trace["final"] = _observation_to_dict(current)
                    trace["stepCount"] = len(trace["steps"])
                    _write_json_atomic(trace_path, trace, overwrite=request.overwrite)
                    trace["trace"] = {"path": str(trace_path)}
                    return trace

                route_target_page = (
                    "heroSelect"
                    if request.target.hero_id is not None
                    and request.target.page_id == "mainMenu"
                    and not hero_parameter_prepared
                    else request.target.page_id
                )
                if current.page_id == route_target_page:
                    raise GameDriverError(
                        "navigationParameterNotVerified",
                        f"Target parameters were not verified on page {current.page_id}.",
                        6,
                    )
                route = self._navigation.find_route(
                    current.page_id,
                    route_target_page,
                    request.target,
                )
                edge = route[0]
                if edge.source_page != current.page_id:
                    raise GameDriverError(
                        "navigationPlannerInvariantFailed",
                        f"Planned edge {edge.action_method} starts at {edge.source_page}, "
                        f"but current page is {current.page_id}.",
                        5,
                    )
                edge_succeeded = False
                for attempt in range(1, edge.max_attempts + 1):
                    before_page = current.page_id
                    try:
                        current = page_object.leave(edge, request.target, runner)
                    except GameDriverError:
                        raise
                    _require_safe_observation(current, f"edge {edge.action_method}")
                    steps = trace["steps"]
                    if isinstance(steps, list) and steps and isinstance(steps[-1], dict):
                        steps[-1]["edge"] = {
                            "sourcePage": edge.source_page,
                            "actionMethod": edge.action_method,
                            "allowedTargetPages": list(edge.allowed_target_pages),
                            "settleRule": edge.settle_rule,
                            "attempt": attempt,
                        }
                    if current.page_id in edge.allowed_target_pages:
                        edge_succeeded = True
                        break
                    if current.page_id == before_page and attempt < edge.max_attempts:
                        continue
                    if current.page_id == before_page:
                        raise GameDriverError(
                            "navigationEdgeDidNotLeavePage",
                            f"Action {edge.action_method} remained on source page "
                            f"{before_page} after {attempt} attempt(s).",
                            6,
                        )
                    raise GameDriverError(
                        "navigationUnexpectedPage",
                        f"Action {edge.action_method} reached {current.page_id}, "
                        f"which is not an allowed target page: "
                        f"{', '.join(edge.allowed_target_pages)}.",
                        6,
                    )
                if not edge_succeeded:
                    raise GameDriverError(
                        "navigationEdgeFailed",
                        f"Action {edge.action_method} did not reach an allowed page.",
                        6,
                    )
            raise GameDriverError(
                "navigationStepLimitExceeded",
                f"Navigation did not reach {request.target.page_id} within "
                f"{request.max_steps} steps.",
                6,
            )
        except GameDriverError as error:
            trace["status"] = "failed"
            trace["error"] = {"code": error.code, "message": error.message}
            trace["final"] = _observation_to_dict(current) if "current" in locals() else None
            trace["stepCount"] = len(trace["steps"])
            _write_json_atomic(trace_path, trace, overwrite=True)
            raise GameDriverError(
                error.code,
                f"{error.message} Evidence: {trace_path}",
                error.exit_code,
            ) from error

    def _capture_initial(
        self,
        request: NavigationRequest,
        output_directory: Path,
    ) -> NavigationObservation:
        self._game_driver.capture(
            CaptureRequest(
                selector=request.selector,
                output_path=output_directory / "initial.png",
                launch_path=request.launch_path,
                activate=True,
                overwrite=request.overwrite,
                settle_ms=request.settle_ms,
                activation_timeout_ms=request.activation_timeout_ms,
                window_timeout_ms=request.window_timeout_ms,
                launch_timeout_ms=request.launch_timeout_ms,
            )
        )
        evidence = read_evidence(output_directory / "initial.json")
        recognition, _ = recognize_image(evidence, self._catalog)
        observation = _observation_from_document(recognition["recognition"])
        _require_safe_observation(observation, "initial observation")
        return observation


class _NavigationActionRunner:
    def __init__(
        self,
        navigator: PageNavigator,
        request: NavigationRequest,
        output_directory: Path,
        trace: dict[str, object],
        interaction: InteractionDriver,
        catalog: VisualCatalog,
    ) -> None:
        self._navigator = navigator
        self._request = request
        self._output_directory = output_directory
        self._trace = trace
        self._interaction = interaction
        self._catalog = catalog
        self._step_number = 0

    def click(
        self,
        element_id: str,
        *,
        expected_page_id: str | None = None,
        expected_view_state_id: str | None = None,
    ) -> NavigationObservation:
        step_directory = self._step_directory("click", element_id)
        result = self._interaction.click(
            ClickRequest(
                selector=self._request.selector,
                element_id=element_id,
                phase=self._request.phase,
                output_directory=step_directory,
                launch_path=self._request.launch_path,
                overwrite=self._request.overwrite,
                expected_page_id=expected_page_id,
                expected_view_state_id=expected_view_state_id,
                settle_ms=self._request.settle_ms,
                activation_timeout_ms=self._request.activation_timeout_ms,
                window_timeout_ms=self._request.window_timeout_ms,
                launch_timeout_ms=self._request.launch_timeout_ms,
                transition_timeout_ms=self._request.transition_timeout_ms,
                poll_interval_ms=self._request.poll_interval_ms,
                stable_sample_count=self._request.stable_sample_count,
                change_threshold=self._request.change_threshold,
                stability_threshold=self._request.stability_threshold,
            ),
            self._catalog,
        )
        return self._record_result(
            "click",
            {"elementId": element_id},
            result,
        )

    def scroll(
        self,
        reference_x: int,
        reference_y: int,
        direction: str,
        *,
        expected_page_id: str | None = None,
        expected_view_state_id: str | None = None,
    ) -> NavigationObservation:
        step_directory = self._step_directory(
            "scroll",
            f"{direction}-{reference_x}-{reference_y}",
        )
        result = self._interaction.scroll_point(
            ScrollPointRequest(
                selector=self._request.selector,
                reference_x=reference_x,
                reference_y=reference_y,
                direction=direction,
                notches=1,
                allow_no_change=False,
                phase=self._request.phase,
                output_directory=step_directory,
                launch_path=self._request.launch_path,
                overwrite=self._request.overwrite,
                expected_page_id=expected_page_id,
                expected_view_state_id=expected_view_state_id,
                settle_ms=self._request.settle_ms,
                activation_timeout_ms=self._request.activation_timeout_ms,
                window_timeout_ms=self._request.window_timeout_ms,
                launch_timeout_ms=self._request.launch_timeout_ms,
                transition_timeout_ms=self._request.transition_timeout_ms,
                poll_interval_ms=self._request.poll_interval_ms,
                stable_sample_count=self._request.stable_sample_count,
                change_threshold=0.005,
                stability_threshold=self._request.stability_threshold,
            ),
            self._catalog,
        )
        return self._record_result(
            "scroll",
            {
                "referencePoint": {"x": reference_x, "y": reference_y},
                "direction": direction,
            },
            result,
        )

    def _step_directory(self, action: str, target: str) -> Path:
        self._step_number += 1
        safe_target = re.sub(r"[^A-Za-z0-9_.-]+", "-", target.replace(".", "-"))
        return self._output_directory / f"step-{self._step_number:03d}-{action}-{safe_target}"

    def _record_result(
        self,
        action: str,
        input_document: dict[str, object],
        result: dict[str, object],
    ) -> NavigationObservation:
        after = result.get("after")
        after_document = after.get("recognition") if isinstance(after, dict) else None
        if not isinstance(after_document, dict):
            raise GameDriverError(
                "navigationRecognitionMissing",
                f"Interaction result for {action} has no after recognition.",
                5,
            )
        observation = _observation_from_document(after_document)
        self._trace["steps"].append(
            {
                "step": self._step_number,
                "action": action,
                "input": input_document,
                "trace": result.get("trace"),
                "observation": _observation_to_dict(observation),
            }
        )
        return observation


def _parse_navigation_catalog(
    document: object,
    path: Path,
    visual_catalog: VisualCatalog,
) -> NavigationCatalog:
    root = _object(document, "navigation catalog root")
    if root.get("schemaVersion") != 1:
        raise ValueError("schemaVersion must be 1")
    catalog_id = _nonempty_string(root, "catalogId")
    catalog_version = _positive_integer(root, "catalogVersion")
    if root.get("visualCatalogId") != visual_catalog.id:
        raise ValueError("visualCatalogId does not match the loaded visual catalog")
    if root.get("visualCatalogVersion") != visual_catalog.version:
        raise ValueError("visualCatalogVersion does not match the loaded visual catalog")
    page_ids = tuple(page.id for page in visual_catalog.pages)
    raw_pages = root.get("pages", list(page_ids))
    if not isinstance(raw_pages, list) or not all(isinstance(item, str) for item in raw_pages):
        raise TypeError("pages must be an array of strings")
    pages = tuple(raw_pages)
    if len(set(pages)) != len(pages):
        raise ValueError("pages must not contain duplicates")
    unknown_pages = set(pages) - set(page_ids)
    if unknown_pages:
        raise ValueError(f"navigation pages are absent from visual catalog: {sorted(unknown_pages)}")

    raw_map_targets = root.get("mapTargets", {})
    if not isinstance(raw_map_targets, dict):
        raise TypeError("mapTargets must be an object")
    visual_map_page = next((page for page in visual_catalog.pages if page.id == "mapSelect"), None)
    valid_map_elements = {
        element.id.removeprefix("mapSelect.")
        for element in (visual_map_page.elements if visual_map_page else ())
        if element.id.startswith("mapSelect.") and element.placements
    }
    map_targets: dict[str, str] = {}
    for map_id, view_state in raw_map_targets.items():
        if not isinstance(map_id, str) or not isinstance(view_state, str):
            raise TypeError("mapTargets keys and values must be strings")
        if map_id not in valid_map_elements:
            raise ValueError(f"map target is not a visible map element: {map_id}")
        if view_state not in {
            state.id for state in (visual_map_page.view_states if visual_map_page else ())
        }:
            raise ValueError(f"map target references unknown view state: {view_state}")
        map_targets[map_id] = view_state

    raw_edges = root.get("edges")
    if not isinstance(raw_edges, list) or not raw_edges:
        raise TypeError("edges must be a non-empty array")
    edges = tuple(_parse_edge(item, pages, visual_catalog) for item in raw_edges)
    edge_keys = [(edge.source_page, edge.action_method) for edge in edges]
    if len(set(edge_keys)) != len(edge_keys):
        raise ValueError("edges must not repeat a sourcePage/actionMethod pair")
    return NavigationCatalog(path, catalog_id, catalog_version, pages, edges, map_targets)


def _parse_edge(
    value: object,
    pages: tuple[str, ...],
    visual_catalog: VisualCatalog,
) -> NavigationEdge:
    edge = _object(value, "navigation edge")
    source_page = _nonempty_string(edge, "sourcePage")
    if source_page not in pages:
        raise ValueError(f"unknown source page: {source_page}")
    action_method = _nonempty_string(edge, "actionMethod")
    raw_targets = edge.get("allowedTargetPages")
    if not isinstance(raw_targets, list) or not raw_targets or not all(
        isinstance(item, str) and item for item in raw_targets
    ):
        raise TypeError("allowedTargetPages must be a non-empty array of strings")
    allowed_targets = tuple(raw_targets)
    if any(target not in pages for target in allowed_targets):
        raise ValueError(f"edge {action_method} targets an unknown page")
    if source_page in allowed_targets:
        raise ValueError(f"edge {action_method} cannot target its source page")
    element = edge.get("element")
    element_template = edge.get("elementTemplate")
    if (element is None) == (element_template is None):
        raise ValueError(f"edge {action_method} requires exactly one element or elementTemplate")
    if element is not None and not isinstance(element, str):
        raise TypeError("element must be a string")
    if element_template is not None and not isinstance(element_template, str):
        raise TypeError("elementTemplate must be a string")
    raw_parameters = edge.get("parameters", [])
    if not isinstance(raw_parameters, list) or not all(isinstance(item, str) for item in raw_parameters):
        raise TypeError("parameters must be an array of strings")
    parameter_names = tuple(raw_parameters)
    raw_allowed_parameters = edge.get("allowedParameters", {})
    if not isinstance(raw_allowed_parameters, dict):
        raise TypeError("allowedParameters must be an object")
    allowed_parameters: dict[str, tuple[str, ...]] = {}
    for name, values in raw_allowed_parameters.items():
        if name not in parameter_names:
            raise ValueError(f"allowed parameter {name} is not declared by the edge")
        if not isinstance(values, list) or not all(isinstance(item, str) for item in values):
            raise TypeError(f"allowed parameter values for {name} must be strings")
        allowed_parameters[name] = tuple(values)
    if element_template is not None:
        if not parameter_names:
            raise ValueError(f"edge {action_method} elementTemplate needs parameters")
        if not all("{" + name + "}" in element_template for name in parameter_names):
            raise ValueError(f"edge {action_method} elementTemplate omits a parameter")
        if any(name not in allowed_parameters or not allowed_parameters[name] for name in parameter_names):
            raise ValueError(f"edge {action_method} must bound every template parameter")

    raw_conditions = edge.get("conditions", {})
    if not isinstance(raw_conditions, dict) or not all(
        isinstance(name, str) and isinstance(value, str)
        for name, value in raw_conditions.items()
    ):
        raise TypeError("conditions must be an object of string values")
    valid_condition_names = {"mapId", "difficultyId", "modeId", "heroId"}
    if set(raw_conditions) - valid_condition_names:
        raise ValueError(f"edge {action_method} uses an unknown target condition")

    settle_rule = _nonempty_string(edge, "settleRule")
    retry_policy = _object(edge.get("retryPolicy"), "retryPolicy")
    max_attempts = _positive_integer(retry_policy, "maxAttempts")
    raw_side_effects = edge.get("sideEffects", [])
    if not isinstance(raw_side_effects, list) or not all(isinstance(item, str) for item in raw_side_effects):
        raise TypeError("sideEffects must be an array of strings")
    raw_evidence = edge.get("evidence")
    if not isinstance(raw_evidence, list) or not raw_evidence:
        raise TypeError("evidence must be a non-empty array")
    evidence: list[dict[str, str]] = []
    for item in raw_evidence:
        evidence_item = _object(item, "edge evidence")
        if evidence_item.get("verified") is not True:
            raise ValueError(f"edge {action_method} has unverified evidence")
        trace_id = _nonempty_string(evidence_item, "traceId")
        after_evidence_id = _nonempty_string(evidence_item, "afterEvidenceId")
        evidence.append({"traceId": trace_id, "afterEvidenceId": after_evidence_id})

    source_elements = {
        element.id: element
        for page in visual_catalog.pages
        if page.id == source_page
        for element in page.elements
    }
    if element is not None:
        source_element = source_elements.get(element)
        if source_element is None:
            raise ValueError(f"edge {action_method} references an element on another page: {element}")
        if source_element.role != "button":
            raise ValueError(f"edge {action_method} element is not a button: {element}")
    elif element_template is not None:
        for parameter_name in parameter_names:
            for parameter_value in allowed_parameters[parameter_name]:
                rendered_element = element_template.format(
                    mapId=parameter_value,
                    heroId=parameter_value,
                )
                source_element = source_elements.get(rendered_element)
                if source_element is None or source_element.role != "button":
                    raise ValueError(
                        f"edge {action_method} template resolves to a non-button or "
                        f"unknown element: {rendered_element}"
                    )
    return NavigationEdge(
        source_page=source_page,
        action_method=action_method,
        allowed_target_pages=allowed_targets,
        element=element,
        element_template=element_template,
        parameter_names=parameter_names,
        allowed_parameters=allowed_parameters,
        conditions=dict(raw_conditions),
        settle_rule=settle_rule,
        max_attempts=max_attempts,
        side_effects=tuple(raw_side_effects),
        evidence=tuple(evidence),
    )


def _observation_from_document(document: object) -> NavigationObservation:
    value = _object(document, "recognition document")
    page = value.get("page")
    page_id = page.get("id") if isinstance(page, dict) else None
    view_state_id: str | None = None
    if isinstance(page, dict):
        view_state = page.get("viewState")
        state = view_state.get("state") if isinstance(view_state, dict) else None
        candidate = state.get("id") if isinstance(state, dict) else None
        if isinstance(candidate, str):
            view_state_id = candidate
    return NavigationObservation(
        status=str(value.get("status", "unknown")),
        page_id=page_id if isinstance(page_id, str) else None,
        view_state_id=view_state_id,
        oracle_eligible=value.get("oracleEligible") is True,
        document=value,
    )


def _edge_matches_target(edge: NavigationEdge, target: NavigationTarget) -> bool:
    values = {
        "mapId": target.map_id,
        "difficultyId": target.difficulty_id,
        "modeId": target.mode_id,
        "heroId": target.hero_id,
    }
    return all(
        values.get(name) is None or values.get(name) == value
        for name, value in edge.conditions.items()
    )


def _require_safe_observation(observation: NavigationObservation, context: str) -> None:
    if observation.is_safe:
        return
    reason = (
        "unknown page"
        if observation.status == "unknown"
        else (
            "ambiguous page"
            if observation.status == "ambiguous"
            else "evidence is not Oracle eligible"
        )
    )
    raise GameDriverError(
        "navigationOracleRequired",
        f"Navigation stopped after {context}: {reason}.",
        6,
    )


def _target_to_dict(target: NavigationTarget) -> dict[str, str]:
    return {
        key: value
        for key, value in (
            ("pageId", target.page_id),
            ("mapId", target.map_id),
            ("difficultyId", target.difficulty_id),
            ("modeId", target.mode_id),
            ("heroId", target.hero_id),
        )
        if value is not None
    }


def _observation_to_dict(observation: NavigationObservation) -> dict[str, object]:
    return {
        "status": observation.status,
        "pageId": observation.page_id,
        "viewStateId": observation.view_state_id,
        "oracleEligible": observation.oracle_eligible,
    }


def _validate_navigation_outputs(output_directory: Path, overwrite: bool) -> None:
    if output_directory.exists() and not output_directory.is_dir():
        raise UsageError(f"--output-dir must name a directory: {output_directory}")
    if overwrite:
        (output_directory / "navigation.json").unlink(missing_ok=True)
        return
    existing = [
        path
        for path in (output_directory / "initial.json", output_directory / "navigation.json")
        if path.exists()
    ]
    if existing:
        raise GameDriverError(
            "outputExists",
            f"Navigation output already exists: {', '.join(str(path) for path in existing)}. "
            "Use --overwrite to replace it.",
            5,
        )


def _write_json_atomic(path: Path, value: dict[str, object], *, overwrite: bool) -> None:
    if path.exists() and not overwrite:
        raise GameDriverError("outputExists", f"Navigation trace already exists: {path}.", 5)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        temporary_path.write_text(
            json.dumps(value, ensure_ascii=False, indent=2) + os.linesep,
            encoding="utf-8",
        )
        os.replace(temporary_path, path)
    finally:
        temporary_path.unlink(missing_ok=True)


def _object(value: object, name: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise TypeError(f"{name} must be an object")
    return value


def _nonempty_string(value: dict[str, Any], name: str) -> str:
    result = value.get(name)
    if not isinstance(result, str) or not result.strip():
        raise TypeError(f"{name} must be a non-empty string")
    return result


def _positive_integer(value: dict[str, Any], name: str) -> int:
    result = value.get(name)
    if not isinstance(result, int) or isinstance(result, bool) or result <= 0:
        raise ValueError(f"{name} must be a positive integer")
    return result


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)
