from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.models import WindowSelector
from betterbtd_game_driver.navigation import (
    HeroSelectPage,
    MapSelectPage,
    NavigationRequest,
    NavigationObservation,
    PageNavigator,
    NavigationTarget,
    load_navigation_catalog,
)
from betterbtd_game_driver.visual_catalog import load_visual_catalog


TOOL_ROOT = Path(__file__).resolve().parents[1]


class NavigationCatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog(verify_templates=False)
        cls.navigation_path = TOOL_ROOT / "navigation" / "page-navigation.json"
        cls.navigation = load_navigation_catalog(visual_catalog=cls.catalog)

    def test_verified_route_uses_one_cross_page_edge_at_a_time(self) -> None:
        route = self.navigation.find_route("mainMenu", "hotkeys")

        self.assertEqual(("openSettings", "openHotkeys"), tuple(item.action_method for item in route))
        self.assertTrue(all(item.source_page not in item.allowed_target_pages for item in self.navigation.edges))

    def test_target_difficulty_filters_candidate_routes(self) -> None:
        route = self.navigation.find_route(
            "mainMenu",
            "mediumModeSelect",
            NavigationTarget(page_id="mediumModeSelect", difficulty_id="medium"),
        )

        self.assertEqual("selectMedium", route[-1].action_method)

    def test_unsupported_parameterized_mode_does_not_fall_back_to_easy(self) -> None:
        with self.assertRaises(GameDriverError) as context:
            self.navigation.find_route(
                "mainMenu",
                "inLevel",
                NavigationTarget(
                    page_id="inLevel",
                    difficulty_id="medium",
                    mode_id="standard",
                ),
            )
        self.assertEqual("navigationRouteNotFound", context.exception.code)

    def test_unverified_edge_is_rejected(self) -> None:
        document = json.loads(self.navigation_path.read_text(encoding="utf-8"))
        document["edges"][0]["evidence"][0]["verified"] = False
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "navigation.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaises(GameDriverError) as context:
                load_navigation_catalog(path, visual_catalog=self.catalog)
        self.assertEqual("navigationCatalogInvalid", context.exception.code)

    def test_self_loop_is_rejected(self) -> None:
        document = json.loads(self.navigation_path.read_text(encoding="utf-8"))
        document["edges"][0]["allowedTargetPages"] = ["mainMenu"]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "navigation.json"
            path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaises(GameDriverError) as context:
                load_navigation_catalog(path, visual_catalog=self.catalog)
        self.assertEqual("navigationCatalogInvalid", context.exception.code)


class PageObjectPreparationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog(verify_templates=False)
        cls.navigation = load_navigation_catalog(visual_catalog=cls.catalog)

    def test_map_preparation_keeps_page_internal_paging_out_of_graph(self) -> None:
        page = MapSelectPage(self.catalog, self.navigation)
        runner = _FakeRunner()
        result = page.prepare(
            NavigationTarget(page_id="difficultySelect", map_id="treeStump"),
            _observation("mapSelect", "mapSelect.beginner01"),
            runner,
        )

        self.assertTrue(result.completed)
        self.assertEqual([("click", "mapSelect.nextPage")], runner.actions)
        self.assertEqual("mapSelect.beginner02", result.observation.view_state_id)

    def test_hero_preparation_scrolls_then_selects_without_leaving_page(self) -> None:
        page = HeroSelectPage(self.catalog)
        runner = _FakeRunner()
        result = page.prepare(
            NavigationTarget(page_id="heroSelect", hero_id="Corvus"),
            _observation("heroSelect", "heroSelect.top"),
            runner,
        )

        self.assertTrue(result.completed)
        self.assertEqual(
            [("scroll", "down"), ("click", "heroSelect.Corvus")],
            runner.actions,
        )
        self.assertEqual("heroSelect.bottom", result.observation.view_state_id)


class NavigationExecutionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog(verify_templates=False)
        cls.navigation = load_navigation_catalog(visual_catalog=cls.catalog)

    def test_executor_retries_source_page_then_replans_from_actual_page(self) -> None:
        interaction = _FakeInteraction(
            [_recognition("mainMenu"), _recognition("settings")]
        )
        navigator = PageNavigator(object(), self.catalog, self.navigation)
        navigator._interaction = interaction
        navigator._capture_initial = lambda request, output: _observation("mainMenu", None)
        with tempfile.TemporaryDirectory() as directory:
            result = navigator.navigate(
                NavigationRequest(
                    selector=WindowSelector(),
                    target=NavigationTarget(page_id="settings"),
                    phase="arrange",
                    output_directory=Path(directory),
                    launch_path=None,
                    overwrite=False,
                )
            )

        self.assertEqual("completed", result["status"])
        self.assertEqual(2, len(interaction.requests))
        self.assertEqual("mainMenu.settings", interaction.requests[0].element_id)

    def test_executor_stops_without_second_input_on_unknown_after_page(self) -> None:
        interaction = _FakeInteraction([_recognition("unknown", status="unknown", oracle=False)])
        navigator = PageNavigator(object(), self.catalog, self.navigation)
        navigator._interaction = interaction
        navigator._capture_initial = lambda request, output: _observation("mainMenu", None)
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(GameDriverError) as context:
                navigator.navigate(
                    NavigationRequest(
                        selector=WindowSelector(),
                        target=NavigationTarget(page_id="settings"),
                        phase="arrange",
                        output_directory=Path(directory),
                        launch_path=None,
                        overwrite=False,
                    )
                )

        self.assertEqual("navigationOracleRequired", context.exception.code)
        self.assertEqual(1, len(interaction.requests))


class _FakeRunner:
    def __init__(self) -> None:
        self.actions: list[tuple[str, str]] = []
        self.current_view_state: str | None = None

    def click(
        self,
        element_id: str,
        *,
        expected_page_id: str | None = None,
        expected_view_state_id: str | None = None,
    ) -> NavigationObservation:
        self.actions.append(("click", element_id))
        observation = _observation(
            expected_page_id or "mapSelect",
            expected_view_state_id or self.current_view_state,
        )
        self.current_view_state = observation.view_state_id
        return observation

    def scroll(
        self,
        reference_x: int,
        reference_y: int,
        direction: str,
        *,
        expected_page_id: str | None = None,
        expected_view_state_id: str | None = None,
    ) -> NavigationObservation:
        self.actions.append(("scroll", direction))
        observation = _observation(expected_page_id or "heroSelect", expected_view_state_id)
        self.current_view_state = observation.view_state_id
        return observation


class _FakeInteraction:
    def __init__(self, recognitions: list[dict[str, object]]) -> None:
        self.recognitions = recognitions
        self.requests = []

    def click(self, request, catalog) -> dict[str, object]:
        self.requests.append(request)
        return {
            "after": {"recognition": self.recognitions.pop(0)},
            "trace": {"path": "fake-operation.json"},
        }

    def scroll(self, request, catalog) -> dict[str, object]:
        raise AssertionError("scroll was not expected")


def _observation(page_id: str, view_state_id: str | None) -> NavigationObservation:
    return NavigationObservation(
        status="matched",
        page_id=page_id,
        view_state_id=view_state_id,
        oracle_eligible=True,
        document={"status": "matched", "page": {"id": page_id}},
    )


def _recognition(
    page_id: str,
    *,
    status: str = "matched",
    oracle: bool = True,
) -> dict[str, object]:
    return {
        "status": status,
        "oracleEligible": oracle,
        "page": {"id": page_id} if status == "matched" else None,
    }


if __name__ == "__main__":
    unittest.main()
