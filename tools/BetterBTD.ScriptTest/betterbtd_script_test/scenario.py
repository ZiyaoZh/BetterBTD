from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path
import re
from typing import Any, Iterable, Mapping, Sequence

from jsonschema import Draft202012Validator


PROTOCOL_NAME = "betterbtd/script-test-scenario"
PROTOCOL_VERSION = 1
CURRENT_CAPABILITIES = frozenset(
    {
        "PageRecognition",
        "ViewStateRecognition",
        "ElementVisibility",
        "ElementState",
    }
)

_PACKAGE_ROOT = Path(__file__).resolve().parent
_TOOL_ROOT = _PACKAGE_ROOT.parent
_REPOSITORY_ROOT = _TOOL_ROOT.parents[1]
_DEFAULT_SCHEMA_PATH = _TOOL_ROOT / "scenario.schema.json"
_DEFAULT_GAME_STATE_CATALOG_PATH = _TOOL_ROOT / "game-state-catalog.json"
_DEFAULT_CATALOG_PATH = (
    _REPOSITORY_ROOT
    / "tools"
    / "BetterBTD.GameDriver"
    / "visual-baselines"
    / "catalog.json"
)
_FORBIDDEN_FIELD_NAMES = frozenset(
    {
        "expectedsha256",
    }
)
_FORBIDDEN_FIELD_FRAGMENTS = (
    "accesskey",
    "apikey",
    "authorization",
    "bearer",
    "credential",
    "password",
    "privatekey",
    "secret",
    "token",
)
_FORBIDDEN_VALUE_PATTERNS = (
    re.compile(r"^\s*bearer\s+\S+\s*$", re.IGNORECASE),
    re.compile(r"\bauthorization\s*:\s*bearer\s+\S+", re.IGNORECASE),
    re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    re.compile(r"\bgh[pousr]_[A-Za-z0-9]{20,}\b"),
)
_ARTIFACT_PLACEHOLDERS = ("{scenarioId}", "{runId}")


class ScenarioValidationError(ValueError):
    def __init__(self, errors: Sequence[str]):
        self.errors = tuple(errors)
        super().__init__("; ".join(self.errors))


@dataclass(frozen=True)
class ScenarioValidationResult:
    scenario_path: Path
    script_path: Path
    artifact_directory: Path
    required_capabilities: frozenset[str]
    available_capabilities: frozenset[str]
    missing_capabilities: frozenset[str]
    expected_game_state: GameState

    @property
    def capability_compatible(self) -> bool:
        return not self.missing_capabilities


@dataclass(frozen=True)
class _CatalogIndex:
    pages: frozenset[str]
    view_state_pages: Mapping[str, str]
    element_pages: Mapping[str, str]
    element_roles: Mapping[str, str]
    visible_elements: frozenset[str]
    element_states: Mapping[str, frozenset[str]]


@dataclass(frozen=True)
class _GameStateCatalog:
    maps: frozenset[str]
    difficulties: frozenset[str]
    modes: frozenset[str]
    modes_by_difficulty: Mapping[str, frozenset[str]]
    heroes: frozenset[str]


@dataclass(frozen=True)
class GameState:
    map: str
    difficulty: str
    mode: str
    hero: str


def validate_scenario(
    scenario_path: Path | str,
    *,
    repository_root: Path | str = _REPOSITORY_ROOT,
    available_capabilities: Iterable[str] = CURRENT_CAPABILITIES,
    require_script_exists: bool = False,
) -> ScenarioValidationResult:
    scenario_file = Path(scenario_path).resolve()
    repository = Path(repository_root).resolve()
    document = _read_json_object(scenario_file, "scenario")
    schema = _read_json_object(_DEFAULT_SCHEMA_PATH, "scenario schema")

    try:
        Draft202012Validator.check_schema(schema)
    except Exception as exception:
        raise ScenarioValidationError(
            [f"scenario schema is invalid: {exception}"]
        ) from exception

    schema_errors = sorted(
        Draft202012Validator(schema).iter_errors(document),
        key=lambda error: tuple(str(part) for part in error.absolute_path),
    )
    if schema_errors:
        raise ScenarioValidationError(
            [_format_schema_error(error) for error in schema_errors]
        )

    catalog = _build_catalog_index(
        _read_json_object(_DEFAULT_CATALOG_PATH, "Game Driver catalog")
    )
    game_state_values = _read_game_state_catalog(_DEFAULT_GAME_STATE_CATALOG_PATH)
    semantic_errors: list[str] = []
    _validate_no_forbidden_fields(document, "$", semantic_errors)
    expected_game_state = _validate_game_state(
        document["arrange"]["gameState"],
        game_state_values,
        semantic_errors,
    )

    predicate_groups = (
        ("$.arrange.readyWhen", document["arrange"]["readyWhen"]),
        ("$.assert.all", document["assert"]["all"]),
        ("$.assert.neverObserved", document["assert"]["neverObserved"]),
        ("$.recover.targetWhen", document["recover"]["targetWhen"]),
    )
    predicates = [
        (path, index, predicate)
        for path, group in predicate_groups
        for index, predicate in enumerate(group)
    ]
    _validate_unique_predicate_ids(predicates, semantic_errors)
    _validate_arrange_ready_predicate(document["arrange"]["readyWhen"], semantic_errors)
    _validate_assertion_timing(
        document["assert"]["all"],
        (
            ("$.arrange.readyWhen", document["arrange"]["readyWhen"]),
            ("$.assert.neverObserved", document["assert"]["neverObserved"]),
            ("$.recover.targetWhen", document["recover"]["targetWhen"]),
        ),
        semantic_errors,
    )

    implied_capabilities: set[str] = set()
    for path, index, predicate in predicates:
        _validate_predicate(
            predicate,
            f"{path}[{index}]",
            catalog,
            implied_capabilities,
            semantic_errors,
        )

    declared_capabilities = frozenset(document["requiredCapabilities"])
    undeclared_capabilities = implied_capabilities - declared_capabilities
    if undeclared_capabilities:
        semantic_errors.append(
            "$.requiredCapabilities must declare capabilities implied by predicates: "
            + ", ".join(sorted(undeclared_capabilities))
        )

    script_path = _resolve_repository_path(
        document["script"]["path"],
        scenario_file.parent,
        repository,
        "$.script.path",
        semantic_errors,
    )
    artifact_template = document["failureArtifacts"]["directory"]
    _validate_artifact_directory_template(artifact_template, semantic_errors)
    artifact_directory = _resolve_repository_path(
        artifact_template,
        repository,
        repository,
        "$.failureArtifacts.directory",
        semantic_errors,
    )
    artifacts_root = (repository / "artifacts").resolve()
    try:
        artifact_relative = artifact_directory.relative_to(artifacts_root)
    except ValueError:
        artifact_relative = None
    if artifact_relative is None or not artifact_relative.parts:
        semantic_errors.append(
            "$.failureArtifacts.directory must be below the repository "
            "artifacts directory"
        )

    if require_script_exists and not script_path.is_file():
        semantic_errors.append(
            f"$.script.path does not name an existing file: {script_path}"
        )

    if semantic_errors:
        raise ScenarioValidationError(semantic_errors)

    available = frozenset(available_capabilities)
    return ScenarioValidationResult(
        scenario_path=scenario_file,
        script_path=script_path,
        artifact_directory=artifact_directory,
        required_capabilities=declared_capabilities,
        available_capabilities=available,
        missing_capabilities=declared_capabilities - available,
        expected_game_state=expected_game_state,
    )


def validate_script_summary(
    expected: GameState,
    non_oracle_diagnostics: Mapping[str, Any],
) -> None:
    mismatches: list[str] = []
    for field, expected_value in (
        ("map", expected.map),
        ("difficulty", expected.difficulty),
        ("mode", expected.mode),
        ("hero", expected.hero),
    ):
        actual_value = non_oracle_diagnostics.get(field)
        if actual_value != expected_value:
            mismatches.append(
                f"script {field} is {actual_value!r}, expected {expected_value!r}"
            )
    if mismatches:
        raise ScenarioValidationError(mismatches)


def _read_json_object(path: Path, description: str) -> dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8") as stream:
            value = json.load(
                stream,
                object_pairs_hook=_reject_duplicate_keys,
                parse_constant=_reject_nonstandard_json_constant,
            )
    except OSError as exception:
        raise ScenarioValidationError(
            [f"cannot read {description} {path}: {exception}"]
        ) from exception
    except (json.JSONDecodeError, ValueError) as exception:
        raise ScenarioValidationError(
            [f"cannot parse {description} {path}: {exception}"]
        ) from exception

    if not isinstance(value, dict):
        raise ScenarioValidationError([f"{description} root must be an object"])
    return value


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON property {key!r}")
        result[key] = value
    return result


def _reject_nonstandard_json_constant(value: str) -> None:
    raise ValueError(f"non-standard JSON constant {value!r}")


def _format_schema_error(error: Any) -> str:
    path = "$"
    for part in error.absolute_path:
        path += f"[{part}]" if isinstance(part, int) else f".{part}"
    return f"{path}: {error.message}"


def _read_game_state_catalog(path: Path) -> _GameStateCatalog:
    document = _read_json_object(path, "game state catalog")
    if document.get("schemaVersion") != 1:
        raise ScenarioValidationError(["game state catalog schemaVersion must be 1"])

    values_by_field: dict[str, frozenset[str]] = {}
    errors: list[str] = []
    for field in ("maps", "difficulties", "modes", "heroes"):
        values = document.get(field)
        if (
            not isinstance(values, list)
            or not values
            or any(not isinstance(value, str) or not value for value in values)
        ):
            errors.append(
                f"game state catalog {field} must be a non-empty string array"
            )
            continue
        unique_values = frozenset(values)
        if len(unique_values) != len(values):
            errors.append(f"game state catalog {field} contains duplicate values")
        values_by_field[field] = unique_values
    raw_modes_by_difficulty = document.get("modesByDifficulty")
    modes_by_difficulty: dict[str, frozenset[str]] = {}
    if not isinstance(raw_modes_by_difficulty, dict):
        errors.append("game state catalog modesByDifficulty must be an object")
    elif "difficulties" in values_by_field and "modes" in values_by_field:
        if set(raw_modes_by_difficulty) != set(values_by_field["difficulties"]):
            errors.append(
                "game state catalog modesByDifficulty keys must match difficulties"
            )
        for difficulty, values in raw_modes_by_difficulty.items():
            if (
                not isinstance(values, list)
                or not values
                or any(not isinstance(value, str) for value in values)
            ):
                errors.append(
                    "game state catalog modesByDifficulty values must be "
                    "non-empty string arrays"
                )
                continue
            unique_values = frozenset(values)
            if len(unique_values) != len(values):
                errors.append(
                    f"game state catalog modesByDifficulty.{difficulty} "
                    "contains duplicate values"
                )
            unknown_modes = unique_values - values_by_field["modes"]
            if unknown_modes:
                errors.append(
                    f"game state catalog modesByDifficulty.{difficulty} has "
                    f"unknown modes: {', '.join(sorted(unknown_modes))}"
                )
            modes_by_difficulty[difficulty] = unique_values
    if errors:
        raise ScenarioValidationError(errors)
    return _GameStateCatalog(
        maps=values_by_field["maps"],
        difficulties=values_by_field["difficulties"],
        modes=values_by_field["modes"],
        modes_by_difficulty=modes_by_difficulty,
        heroes=values_by_field["heroes"],
    )


def _validate_game_state(
    value: Mapping[str, Any],
    catalog: _GameStateCatalog,
    errors: list[str],
) -> GameState:
    game_state = GameState(
        map=value["map"],
        difficulty=value["difficulty"],
        mode=value["mode"],
        hero=value["hero"],
    )
    for field, actual, known_values in (
        ("map", game_state.map, catalog.maps),
        ("difficulty", game_state.difficulty, catalog.difficulties),
        ("mode", game_state.mode, catalog.modes),
        ("hero", game_state.hero, catalog.heroes),
    ):
        if actual not in known_values:
            errors.append(f"$.arrange.gameState.{field} has unknown value {actual!r}")
    allowed_modes = catalog.modes_by_difficulty.get(game_state.difficulty)
    if allowed_modes is not None and game_state.mode not in allowed_modes:
        errors.append(
            f"$.arrange.gameState.mode {game_state.mode!r} is not valid for "
            f"difficulty {game_state.difficulty!r}"
        )
    return game_state


def _build_catalog_index(catalog: Mapping[str, Any]) -> _CatalogIndex:
    pages: set[str] = set()
    view_state_pages: dict[str, str] = {}
    element_pages: dict[str, str] = {}
    element_roles: dict[str, str] = {}
    visible_elements: set[str] = set()
    element_states: dict[str, frozenset[str]] = {}
    errors: list[str] = []

    if catalog.get("schemaVersion") not in (1, 2):
        errors.append("Game Driver catalog schemaVersion must be 1 or 2")
    if catalog.get("catalogId") != "btd6-ui-independent":
        errors.append(
            "Game Driver catalog catalogId must be 'btd6-ui-independent'"
        )
    game = catalog.get("game")
    if not isinstance(game, dict) or game.get("id") != "BloonsTD6":
        errors.append("Game Driver catalog game.id must be 'BloonsTD6'")
    catalog_version = catalog.get("catalogVersion")
    if (
        not isinstance(catalog_version, int)
        or isinstance(catalog_version, bool)
        or catalog_version <= 0
    ):
        errors.append("Game Driver catalog catalogVersion must be a positive integer")
    reference_space = catalog.get("referenceSpace")
    if not isinstance(reference_space, dict) or (
        reference_space.get("id") != "btd6Reference1920x1080"
        or reference_space.get("width") != 1920
        or reference_space.get("height") != 1080
    ):
        errors.append(
            "Game Driver catalog referenceSpace must be btd6Reference1920x1080"
        )

    raw_pages = catalog.get("pages")
    if not isinstance(raw_pages, list):
        raise ScenarioValidationError(
            ["Game Driver catalog must contain a pages array"]
        )
    if not raw_pages:
        errors.append("Game Driver catalog pages must not be empty")

    for page_index, page in enumerate(raw_pages):
        if not isinstance(page, dict) or not isinstance(page.get("id"), str):
            errors.append(f"Game Driver catalog pages[{page_index}] has no string id")
            continue
        page_id = page["id"]
        if page_id in pages:
            errors.append(f"Game Driver catalog contains duplicate page id {page_id!r}")
            continue
        pages.add(page_id)
        if page.get("kind") not in ("page", "modal"):
            errors.append(f"Game Driver page {page_id!r} has an invalid kind")
        anchors = page.get("anchors")
        if not isinstance(anchors, list) or not anchors:
            errors.append(f"Game Driver page {page_id!r} has no anchors")
            anchors = []
        minimum_matched_anchors = page.get("minimumMatchedAnchors")
        if (
            not isinstance(minimum_matched_anchors, int)
            or isinstance(minimum_matched_anchors, bool)
            or minimum_matched_anchors <= 0
        ):
            errors.append(
                f"Game Driver page {page_id!r} has invalid minimumMatchedAnchors"
            )
        elif minimum_matched_anchors > sum(
            isinstance(anchor, dict) and anchor.get("pageAnchor", True)
            for anchor in anchors
        ):
            errors.append(
                f"Game Driver page {page_id!r} minimumMatchedAnchors exceeds "
                "its page anchor count"
            )
        minimum_score = page.get("minimumScore")
        if (
            not isinstance(minimum_score, (int, float))
            or isinstance(minimum_score, bool)
            or not 0 <= minimum_score <= 1
        ):
            errors.append(f"Game Driver page {page_id!r} has invalid minimumScore")
        holdout = page.get("positiveHoldout")
        if not isinstance(holdout, dict) or not all(
            isinstance(holdout.get(field), str) and holdout[field]
            for field in ("evidence", "evidenceId", "imageSha256")
        ):
            errors.append(f"Game Driver page {page_id!r} has invalid positiveHoldout")

        for view_state in page.get("viewStates", []):
            if not isinstance(view_state, dict) or not isinstance(
                view_state.get("id"), str
            ):
                errors.append(f"Game Driver page {page_id!r} has an invalid view state")
                continue
            view_state_id = view_state["id"]
            previous_page = view_state_pages.setdefault(view_state_id, page_id)
            if previous_page != page_id:
                errors.append(
                    f"Game Driver view state {view_state_id!r} belongs to "
                    "multiple pages"
                )

        raw_elements = page.get("elements")
        if not isinstance(raw_elements, list):
            errors.append(f"Game Driver page {page_id!r} has no elements array")
            raw_elements = []
        for element in raw_elements:
            if not isinstance(element, dict) or not isinstance(element.get("id"), str):
                errors.append(f"Game Driver page {page_id!r} has an invalid element")
                continue
            element_id = element["id"]
            previous_page = element_pages.setdefault(element_id, page_id)
            if previous_page != page_id:
                errors.append(
                    f"Game Driver element {element_id!r} belongs to multiple pages"
                )
                continue
            role = element.get("role")
            if isinstance(role, str):
                element_roles[element_id] = role

            placements = element.get("placements", [])
            has_visibility_anchors = _has_anchor_ids(element) or any(
                isinstance(placement, dict) and _has_anchor_ids(placement)
                for placement in placements
            )
            if has_visibility_anchors:
                visible_elements.add(element_id)

            states: set[str] = set()
            _collect_state_ids(element.get("states"), states)
            for placement in placements:
                if isinstance(placement, dict):
                    _collect_state_ids(placement.get("states"), states)
            if states:
                element_states[element_id] = frozenset(states)

    if errors:
        raise ScenarioValidationError(errors)
    return _CatalogIndex(
        pages=frozenset(pages),
        view_state_pages=view_state_pages,
        element_pages=element_pages,
        element_roles=element_roles,
        visible_elements=frozenset(visible_elements),
        element_states=element_states,
    )


def _has_anchor_ids(value: Mapping[str, Any]) -> bool:
    anchor_ids = value.get("anchorIds")
    return isinstance(anchor_ids, list) and any(
        isinstance(item, str) for item in anchor_ids
    )


def _collect_state_ids(value: Any, states: set[str]) -> None:
    if not isinstance(value, list):
        return
    for state in value:
        if isinstance(state, dict) and isinstance(state.get("id"), str):
            states.add(state["id"])


def _validate_no_forbidden_fields(
    value: Any,
    path: str,
    errors: list[str],
) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            normalized_key = "".join(
                character for character in key.lower() if character.isalnum()
            )
            if normalized_key in _FORBIDDEN_FIELD_NAMES or any(
                fragment in normalized_key
                for fragment in _FORBIDDEN_FIELD_FRAGMENTS
            ):
                errors.append(f"{path}.{key} is forbidden in scenario files")
            _validate_no_forbidden_fields(child, f"{path}.{key}", errors)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            _validate_no_forbidden_fields(child, f"{path}[{index}]", errors)
    elif isinstance(value, str) and any(
        pattern.search(value) for pattern in _FORBIDDEN_VALUE_PATTERNS
    ):
        errors.append(f"{path} contains credential-like content")


def _validate_unique_predicate_ids(
    predicates: Sequence[tuple[str, int, Mapping[str, Any]]],
    errors: list[str],
) -> None:
    seen: dict[str, str] = {}
    for path, index, predicate in predicates:
        predicate_id = predicate["id"]
        current_path = f"{path}[{index}].id"
        first_path = seen.setdefault(predicate_id, current_path)
        if first_path != current_path:
            errors.append(
                f"{current_path} duplicates predicate id {predicate_id!r} "
                f"from {first_path}"
            )


def _validate_arrange_ready_predicate(
    predicates: Sequence[Mapping[str, Any]],
    errors: list[str],
) -> None:
    if not any(predicate["kind"] == "Page" for predicate in predicates):
        errors.append("$.arrange.readyWhen must contain a Page predicate")


def _validate_assertion_timing(
    positive_assertions: Sequence[Mapping[str, Any]],
    other_predicate_groups: Sequence[
        tuple[str, Sequence[Mapping[str, Any]]]
    ],
    errors: list[str],
) -> None:
    for index, predicate in enumerate(positive_assertions):
        for field, expected in (
            ("quantifier", "Eventually"),
            ("observationWindow", None),
        ):
            if field not in predicate:
                errors.append(f"$.assert.all[{index}].{field} is required")
            elif expected is not None and predicate[field] != expected:
                errors.append(
                    f"$.assert.all[{index}].{field} must be {expected!r}"
                )

    for path, predicates in other_predicate_groups:
        for index, predicate in enumerate(predicates):
            for field in ("quantifier", "observationWindow"):
                if field in predicate:
                    errors.append(
                        f"{path}[{index}].{field} is only valid in $.assert.all"
                    )


def _validate_artifact_directory_template(
    value: str,
    errors: list[str],
) -> None:
    for placeholder in _ARTIFACT_PLACEHOLDERS:
        count = value.split("/").count(placeholder)
        if count != 1:
            errors.append(
                "$.failureArtifacts.directory must contain exactly one "
                f"{placeholder} path segment"
            )


def _validate_predicate(
    predicate: Mapping[str, Any],
    path: str,
    catalog: _CatalogIndex,
    implied_capabilities: set[str],
    errors: list[str],
) -> None:
    kind = predicate["kind"]
    implied_capabilities.add("PageRecognition")

    if kind == "Page":
        page_ids = (
            [predicate["pageId"]]
            if predicate["operator"] == "Equals"
            else predicate["pageIds"]
        )
        for page_id in page_ids:
            if page_id not in catalog.pages:
                errors.append(f"{path} references unknown page {page_id!r}")
        return

    if kind == "ViewState":
        implied_capabilities.add("ViewStateRecognition")
        page_id = predicate["pageId"]
        view_state_id = predicate["viewStateId"]
        if page_id not in catalog.pages:
            errors.append(f"{path} references unknown page {page_id!r}")
        actual_page = catalog.view_state_pages.get(view_state_id)
        if actual_page is None:
            errors.append(f"{path} references unknown view state {view_state_id!r}")
        elif actual_page != page_id:
            errors.append(
                f"{path} view state {view_state_id!r} belongs to page {actual_page!r}, "
                f"not {page_id!r}"
            )
        return

    element_id = predicate["elementId"]
    if element_id not in catalog.element_pages:
        errors.append(f"{path} references unknown element {element_id!r}")
        return

    if kind == "Element":
        implied_capabilities.add("ElementVisibility")
        if element_id not in catalog.visible_elements:
            errors.append(
                f"{path} element {element_id!r} has no independent visibility detector"
            )
        return

    if kind == "ElementState":
        implied_capabilities.update({"ElementVisibility", "ElementState"})
        states = catalog.element_states.get(element_id, frozenset())
        state = predicate["state"]
        if state not in states:
            errors.append(
                f"{path} references unknown state {state!r} for element {element_id!r}"
            )
        return

    if kind == "ElementNumber":
        implied_capabilities.add("ElementNumber")
        if catalog.element_roles.get(element_id) != "value":
            errors.append(
                f"{path} element {element_id!r} is not declared with role 'value'"
            )


def _resolve_repository_path(
    raw_path: str,
    base_directory: Path,
    repository_root: Path,
    field_path: str,
    errors: list[str],
) -> Path:
    value = Path(raw_path)
    if value.is_absolute():
        errors.append(f"{field_path} must be relative")
        return value.resolve()

    resolved = (base_directory / value).resolve()
    try:
        resolved.relative_to(repository_root)
    except ValueError:
        errors.append(f"{field_path} must resolve within the repository")
    return resolved
