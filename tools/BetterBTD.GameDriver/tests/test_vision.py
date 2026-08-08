from pathlib import Path
import tempfile
import unittest
from dataclasses import replace

from PIL import Image

from betterbtd_game_driver.evidence import read_evidence
from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.vision import recognize_image, write_annotation
from betterbtd_game_driver.visual_catalog import (
    DEFAULT_CATALOG_PATH,
    VisualCatalog,
    VisualPage,
    load_visual_catalog,
)
from visual_test_support import write_test_evidence


SAMPLE_ROOT = DEFAULT_CATALOG_PATH.parent / "samples"

MAP_SELECT_VIEWPORTS = (
    ("map-select.zh-CN.holdout.json", "mapSelect.beginner01", "beginner", ("monkeyMeadow", "inTheLoop", "skullTweak", "threeMilesRound", "spaPits", "tinkerTon")),
    ("map-select-page-02.zh-CN.holdout.json", "mapSelect.beginner02", "beginner", ("treeStump", "townCenter", "middleOfTheRoad", "oneTwoTree", "scrapYard", "theCabin")),
    ("map-select-page-03.zh-CN.holdout.json", "mapSelect.beginner03", "beginner", ("resort", "skates", "lotusIsland", "candyFalls", "winterPark", "carved")),
    ("map-select-page-04.zh-CN.holdout.json", "mapSelect.beginner04", "beginner", ("parkPath", "alpineRun", "frozenOver", "cubism", "fourCircles", "hedge")),
    ("map-select-page-05.zh-CN.holdout.json", "mapSelect.beginner05", "beginner", ("logs", "endOfTheRoad")),
    ("map-select-page-06.zh-CN.holdout.json", "mapSelect.intermediate01", "intermediate", ("lostCrevasse", "luminousCove", "ancientPortal", "sulfurSprings", "waterPark", "polyphemus")),
    ("map-select-page-07.zh-CN.holdout.json", "mapSelect.intermediate02", "intermediate", ("coveredGarden", "quarry", "quietStreet", "bloonariusPrime", "balance", "encrypted")),
    ("map-select-page-08.zh-CN.holdout.json", "mapSelect.intermediate03", "intermediate", ("bazaar", "adorasTemple", "springSpring", "kartMonkey", "moonLanding", "haunted")),
    ("map-select-page-09.zh-CN.holdout.json", "mapSelect.intermediate04", "intermediate", ("downstream", "firingRange", "cracked", "streambed", "chutes", "rake")),
    ("map-select-page-10.zh-CN.holdout.json", "mapSelect.intermediate05", "intermediate", ("spiceIslands",)),
    ("map-select-page-11.zh-CN.holdout.json", "mapSelect.advanced01", "advanced", ("ascent", "mushroomGortto", "partyParade", "sunsetGulch", "enchantedGlade", "lastResort")),
    ("map-select-page-12.zh-CN.holdout.json", "mapSelect.advanced02", "advanced", ("castleRevenge", "darkPath", "erosion", "midnightMansion", "sunkenColumns", "xFactor")),
    ("map-select-page-13.zh-CN.holdout.json", "mapSelect.advanced03", "advanced", ("mesa", "geared", "spillway", "cargo", "patsPond", "peninsula")),
    ("map-select-page-14.zh-CN.holdout.json", "mapSelect.advanced04", "advanced", ("highFinance", "anotherBrick", "offTheCoast", "cornfield", "underground")),
    ("map-select-page-15.zh-CN.holdout.json", "mapSelect.expert01", "expert", ("trickyTracks", "glacialTrail", "darkDungeon", "sanctuary", "ravine", "floodedValley")),
    ("map-select-page-16.zh-CN.holdout.json", "mapSelect.expert02", "expert", ("infernal", "bloodyPuddles", "workshop", "quad", "darkCastle", "muddyPuddles")),
    ("map-select-page-17.zh-CN.holdout.json", "mapSelect.expert03", "expert", ("ouch",)),
)


class VisualRecognitionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_visual_catalog()

    def test_real_holdout_frame_matches_welcome(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "welcome.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("welcome", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.97)
        self.assertEqual(5, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        start = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "welcome.start"
        )
        self.assertEqual("visible", start["visibility"])
        self.assertTrue(start["visible"])

    def test_real_holdout_frame_matches_modified_client_warning(self) -> None:
        evidence = read_evidence(
            SAMPLE_ROOT / "modified-client-warning.zh-CN.holdout.json"
        )

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual(
            "modifiedClientWarning",
            result["recognition"]["page"]["id"],
        )
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(5, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        continue_button = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "modifiedClientWarning.continue"
        )
        self.assertEqual("visible", continue_button["visibility"])
        self.assertTrue(continue_button["visible"])

    def test_real_holdout_frame_matches_main_menu(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("mainMenu", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(19, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        self.assertEqual(
            "mainMenu",
            result["recognition"]["candidates"][0]["id"],
        )
        start = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "mainMenu.start"
        )
        player = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "mainMenu.playerSummary"
        )
        self.assertEqual("visible", start["visibility"])
        self.assertTrue(start["visible"])
        self.assertEqual("notEvaluated", player["visibility"])
        self.assertIsNone(player["visible"])

    def test_real_loading_frame_is_unknown(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "loading.unknown.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNone(match)
        self.assertEqual("unknown", result["recognition"]["status"])
        self.assertFalse(result["recognition"]["oracleEligible"])

    def test_real_holdout_frame_matches_map_select(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "map-select.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("mapSelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(102, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        view_state = result["recognition"]["page"]["viewState"]
        self.assertEqual("matched", view_state["status"])
        self.assertEqual("mapSelect.beginner01", view_state["state"]["id"])
        monkey_meadow = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "mapSelect.monkeyMeadow"
        )
        self.assertEqual("visible", monkey_meadow["visibility"])
        self.assertTrue(monkey_meadow["visible"])

    def test_all_real_map_holdouts_match_viewports_maps_and_controls(self) -> None:
        all_map_ids = {
            f"mapSelect.{map_name}"
            for _, _, _, map_names in MAP_SELECT_VIEWPORTS
            for map_name in map_names
        }
        action_points = (
            {"x": 535, "y": 270},
            {"x": 955, "y": 270},
            {"x": 1375, "y": 270},
            {"x": 535, "y": 585},
            {"x": 955, "y": 585},
            {"x": 1375, "y": 585},
        )
        categories = ("beginner", "intermediate", "advanced", "expert")

        for evidence_name, expected_view, expected_tier, map_names in MAP_SELECT_VIEWPORTS:
            with self.subTest(evidence=evidence_name):
                evidence = read_evidence(SAMPLE_ROOT / evidence_name)

                result, match = recognize_image(evidence, self.catalog)

                self.assertIsNotNone(match)
                recognition = result["recognition"]
                self.assertEqual("matched", recognition["status"])
                self.assertTrue(recognition["oracleEligible"])
                self.assertEqual("mapSelect", recognition["page"]["id"])
                self.assertGreater(recognition["page"]["score"], 0.99)
                view_state = recognition["page"]["viewState"]
                self.assertEqual("matched", view_state["status"])
                self.assertTrue(view_state["oracleEligible"])
                self.assertEqual(expected_view, view_state["state"]["id"])
                self.assertGreater(view_state["state"]["score"], 0.99)

                elements = {
                    element["id"]: element for element in recognition["elements"]
                }
                current_map_ids = {f"mapSelect.{name}" for name in map_names}
                for slot, map_name in enumerate(map_names):
                    element = elements[f"mapSelect.{map_name}"]
                    if map_name == "ascent":
                        self.assertEqual("button", element["role"])
                        self.assertEqual("notVisible", element["visibility"])
                        self.assertFalse(element["visible"])
                        self.assertEqual(action_points[slot], element["actionPointClient"])
                        locked = elements["mapSelect.ascentLocked"]
                        self.assertEqual("status", locked["role"])
                        self.assertEqual("visible", locked["visibility"])
                        self.assertIsNone(locked["actionPointClient"])
                        self.assertEqual("locked", locked["state"]["id"])
                    else:
                        self.assertEqual("visible", element["visibility"])
                        self.assertTrue(element["visible"])
                        self.assertEqual("button", element["role"])
                        self.assertEqual(action_points[slot], element["actionPointClient"])
                for map_id in all_map_ids - current_map_ids:
                    element = elements[map_id]
                    self.assertEqual("viewStateUnknown", element["visibility"])
                    self.assertIsNone(element["actionPointClient"])

                for category in categories:
                    element = elements[f"mapSelect.{category}"]
                    self.assertEqual("visible", element["visibility"])
                    self.assertEqual(
                        "selected" if category == expected_tier else "unselected",
                        element["state"]["id"],
                    )
                self.assertEqual(
                    "enabled",
                    elements["mapSelect.doubleCash"]["state"]["id"],
                )
                self.assertEqual(
                    "disabled",
                    elements["mapSelect.autoStart"]["state"]["id"],
                )
                for element_id in (
                    "mapSelect.pageIndicator",
                    "mapSelect.changeHero",
                    "mapSelect.friends",
                    "mapSelect.community",
                ):
                    self.assertEqual("visible", elements[element_id]["visibility"])

    def test_real_unlocked_ascent_holdout_is_actionable(self) -> None:
        evidence = read_evidence(
            SAMPLE_ROOT / "map-select-ascent-unlocked.zh-CN.holdout.json"
        )

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        recognition = result["recognition"]
        self.assertEqual("matched", recognition["status"])
        self.assertTrue(recognition["oracleEligible"])
        self.assertEqual("mapSelect", recognition["page"]["id"])
        view_state = recognition["page"]["viewState"]
        self.assertEqual("matched", view_state["status"])
        self.assertEqual("mapSelect.advanced01", view_state["state"]["id"])
        self.assertGreater(view_state["state"]["score"], 0.99)
        elements = {
            element["id"]: element for element in recognition["elements"]
        }
        ascent = elements["mapSelect.ascent"]
        self.assertEqual("button", ascent["role"])
        self.assertEqual("visible", ascent["visibility"])
        self.assertEqual({"x": 535, "y": 270}, ascent["actionPointClient"])
        self.assertEqual("notVisible", elements["mapSelect.ascentLocked"]["visibility"])

    def test_advanced_category_detectors_are_mutually_exclusive(self) -> None:
        cases = (
            (
                "map-select-page-11.zh-CN.holdout.json",
                "mapSelect.advancedSelected",
                "mapSelect.advancedUnselected",
            ),
            (
                "map-select-category-clean.zh-CN.holdout.json",
                "mapSelect.advancedUnselected",
                "mapSelect.advancedSelected",
            ),
        )

        for evidence_name, matched_anchor_id, rejected_anchor_id in cases:
            with self.subTest(evidence=evidence_name):
                evidence = read_evidence(SAMPLE_ROOT / evidence_name)
                result, _ = recognize_image(evidence, self.catalog)
                page = result["recognition"]["page"]
                anchors = {anchor["id"]: anchor for anchor in page["anchors"]}

                self.assertTrue(anchors[matched_anchor_id]["matched"])
                self.assertFalse(anchors[rejected_anchor_id]["matched"])
                self.assertGreaterEqual(anchors[matched_anchor_id]["score"], 0.95)
                self.assertLess(anchors[rejected_anchor_id]["score"], 0.95)

    def test_real_map_option_holdout_distinguishes_opposite_states(self) -> None:
        evidence = read_evidence(
            SAMPLE_ROOT / "map-select-options-opposite.zh-CN.holdout.json"
        )

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        elements = {
            element["id"]: element for element in result["recognition"]["elements"]
        }
        self.assertEqual("disabled", elements["mapSelect.doubleCash"]["state"]["id"])
        self.assertEqual("enabled", elements["mapSelect.autoStart"]["state"]["id"])

    def test_real_holdout_frame_matches_difficulty_select(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "difficulty-select.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("difficultySelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(9, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        for element_id in (
            "difficultySelect.easy",
            "difficultySelect.medium",
            "difficultySelect.hard",
        ):
            element = next(
                candidate
                for candidate in result["recognition"]["elements"]
                if candidate["id"] == element_id
            )
            self.assertEqual("visible", element["visibility"])
            self.assertTrue(element["visible"])

    def test_real_holdout_frame_matches_easy_mode_select(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "easy-mode-select.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("easyModeSelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(9, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        for element_id in (
            "easyModeSelect.standard",
            "easyModeSelect.primaryOnly",
            "easyModeSelect.deflation",
        ):
            element = next(
                candidate
                for candidate in result["recognition"]["elements"]
                if candidate["id"] == element_id
            )
            self.assertEqual("visible", element["visibility"])
            self.assertTrue(element["visible"])

    def test_real_holdout_frame_matches_in_level(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "in-level.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("inLevel", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(8, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        settings = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "inLevel.settings"
        )
        cash = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "inLevel.cash"
        )
        self.assertEqual("visible", settings["visibility"])
        self.assertTrue(settings["visible"])
        self.assertEqual("notEvaluated", cash["visibility"])

    def test_real_holdout_frame_matches_victory_player_stats(self) -> None:
        evidence = read_evidence(
            SAMPLE_ROOT / "victory-player-stats.zh-CN.holdout.json"
        )

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        recognition = result["recognition"]
        self.assertEqual("matched", recognition["status"])
        self.assertTrue(recognition["oracleEligible"])
        self.assertEqual("victoryPlayerStats", recognition["page"]["id"])
        self.assertEqual("modal", recognition["page"]["kind"])
        self.assertGreater(recognition["page"]["score"], 0.99)
        self.assertEqual(4, len(recognition["elements"]))
        next_button = next(
            element
            for element in recognition["elements"]
            if element["id"] == "victoryPlayerStats.next"
        )
        self.assertEqual("visible", next_button["visibility"])
        self.assertTrue(next_button["visible"])
        self.assertEqual({"x": 960, "y": 905}, next_button["actionPointClient"])

    def test_victory_player_stats_page_ignores_dynamic_values_and_labels(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(
                SAMPLE_ROOT / "victory-player-stats.zh-CN.holdout.png"
            ) as source:
                modified = source.convert("RGB")
                modified.paste((235, 80, 20), (815, 95, 1105, 220))
                modified.paste((70, 110, 160), (800, 275, 1125, 790))
                modified.paste((30, 210, 20), (880, 875, 1040, 935))
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "victory-stats-dynamic-content-changed",
                modified,
            )
            evidence = read_evidence(metadata_path)

            result, match = recognize_image(evidence, self.catalog)

            self.assertIsNotNone(match)
            recognition = result["recognition"]
            self.assertEqual("matched", recognition["status"])
            self.assertEqual("victoryPlayerStats", recognition["page"]["id"])
            next_button = next(
                element
                for element in recognition["elements"]
                if element["id"] == "victoryPlayerStats.next"
            )
            self.assertEqual("notVisible", next_button["visibility"])
            self.assertFalse(next_button["visible"])

    def test_real_holdout_frame_matches_victory_summary_actions(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "victory-summary.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        recognition = result["recognition"]
        self.assertEqual("matched", recognition["status"])
        self.assertTrue(recognition["oracleEligible"])
        self.assertEqual("victorySummary", recognition["page"]["id"])
        self.assertEqual("modal", recognition["page"]["kind"])
        self.assertGreater(recognition["page"]["score"], 0.99)
        self.assertEqual(6, len(recognition["elements"]))
        elements = {element["id"]: element for element in recognition["elements"]}
        for element_id, action_point in (
            ("victorySummary.home", {"x": 720, "y": 850}),
            ("victorySummary.browseMaps", {"x": 960, "y": 850}),
            ("victorySummary.freeplay", {"x": 1200, "y": 850}),
        ):
            element = elements[element_id]
            self.assertEqual("visible", element["visibility"])
            self.assertTrue(element["visible"])
            self.assertEqual(action_point, element["actionPointClient"])
        self.assertEqual("notEvaluated", elements["victorySummary.reward"]["visibility"])

    def test_freeplay_modal_takes_precedence_over_visible_in_level_hud(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "freeplay-prompt.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        recognition = result["recognition"]
        self.assertEqual("matched", recognition["status"])
        self.assertTrue(recognition["oracleEligible"])
        self.assertEqual("freeplayPrompt", recognition["page"]["id"])
        self.assertEqual("modal", recognition["page"]["kind"])
        self.assertGreater(recognition["page"]["score"], 0.99)
        in_level = next(
            candidate
            for candidate in recognition["candidates"]
            if candidate["id"] == "inLevel"
        )
        self.assertTrue(in_level["matched"])
        self.assertEqual("page", in_level["kind"])
        ok_button = next(
            element
            for element in recognition["elements"]
            if element["id"] == "freeplayPrompt.ok"
        )
        self.assertEqual("visible", ok_button["visibility"])
        self.assertTrue(ok_button["visible"])
        self.assertEqual({"x": 960, "y": 755}, ok_button["actionPointClient"])

    def test_real_holdout_frame_matches_medium_mode_select(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "medium-mode-select.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("mediumModeSelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(11, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        for element_id in (
            "mediumModeSelect.standard",
            "mediumModeSelect.militaryOnly",
            "mediumModeSelect.apopalypse",
            "mediumModeSelect.reverse",
            "mediumModeSelect.sandbox",
        ):
            element = next(
                candidate
                for candidate in result["recognition"]["elements"]
                if candidate["id"] == element_id
            )
            self.assertEqual("visible", element["visibility"])
            self.assertTrue(element["visible"])

    def test_real_holdout_frame_matches_hard_mode_select(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "hard-mode-select.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("hardModeSelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(14, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        for element_id in (
            "hardModeSelect.standard",
            "hardModeSelect.magicOnly",
            "hardModeSelect.doubleHpMoabs",
            "hardModeSelect.halfCash",
            "hardModeSelect.alternateBloonsRounds",
            "hardModeSelect.impoppable",
            "hardModeSelect.chimps",
        ):
            element = next(
                candidate
                for candidate in result["recognition"]["elements"]
                if candidate["id"] == element_id
            )
            self.assertEqual("visible", element["visibility"])
            self.assertTrue(element["visible"])

    def test_real_holdout_frame_matches_hero_select(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "hero-select.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("heroSelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(3, result["recognition"]["page"]["matchedAnchorCount"])
        self.assertEqual(27, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        view_state = result["recognition"]["page"]["viewState"]
        self.assertEqual("matched", view_state["status"])
        self.assertEqual("heroSelect.top", view_state["state"]["id"])
        for element_id in (
            "heroSelect.Quincy",
            "heroSelect.Gwendolin",
            "heroSelect.DanDeMonk",
            "heroSelect.Silas",
            "heroSelect.AdmiralBrickell",
            "heroSelect.back",
        ):
            element = next(
                candidate
                for candidate in result["recognition"]["elements"]
                if candidate["id"] == element_id
            )
            self.assertEqual("visible", element["visibility"])
            self.assertTrue(element["visible"])
        selected = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "heroSelect.selected"
        )
        choose = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "heroSelect.choose"
        )
        self.assertEqual("visible", selected["visibility"])
        self.assertEqual("notVisible", choose["visibility"])

    def test_real_hero_frames_match_view_states_and_all_action_coordinates(self) -> None:
        top_actions = {
            "Quincy": (100, 220),
            "Gwendolin": (255, 220),
            "StrikerJones": (405, 220),
            "ObynGreenfoot": (100, 415),
            "DanDeMonk": (255, 415),
            "Benjamin": (405, 415),
            "PatFusty": (100, 605),
            "CaptainChurchill": (255, 605),
            "Ezili": (405, 605),
            "Silas": (100, 800),
            "Etienne": (255, 800),
            "Sauda": (405, 800),
            "Rosalia": (100, 990),
            "Adora": (255, 990),
            "AdmiralBrickell": (405, 990),
        }
        bottom_actions = {
            "ObynGreenfoot": (100, 160),
            "DanDeMonk": (255, 160),
            "Benjamin": (405, 160),
            "PatFusty": (100, 330),
            "CaptainChurchill": (255, 330),
            "Ezili": (405, 330),
            "Silas": (100, 520),
            "Etienne": (255, 520),
            "Sauda": (405, 520),
            "Rosalia": (100, 710),
            "Adora": (255, 710),
            "AdmiralBrickell": (405, 710),
            "Psi": (100, 900),
            "Geraldo": (255, 900),
            "Corvus": (405, 900),
        }
        cases = (
            ("hero-select.zh-CN.json", "heroSelect.top", top_actions),
            ("hero-select.zh-CN.holdout.json", "heroSelect.top", top_actions),
            ("hero-select-bottom.zh-CN.json", "heroSelect.bottom", bottom_actions),
            (
                "hero-select-bottom.zh-CN.holdout.json",
                "heroSelect.bottom",
                bottom_actions,
            ),
        )
        all_heroes = set(top_actions) | set(bottom_actions)

        for evidence_name, expected_view_state, expected_actions in cases:
            with self.subTest(evidence=evidence_name):
                evidence = read_evidence(SAMPLE_ROOT / evidence_name)
                result, match = recognize_image(evidence, self.catalog)
                recognition = result["recognition"]

                self.assertIsNotNone(match)
                self.assertEqual("matched", recognition["status"])
                self.assertTrue(recognition["oracleEligible"])
                self.assertEqual("heroSelect", recognition["page"]["id"])
                page = recognition["page"]
                page_anchor_scores = [
                    anchor["score"]
                    for anchor in page["anchors"]
                    if anchor["pageAnchor"]
                ]
                self.assertAlmostEqual(
                    sum(page_anchor_scores) / len(page_anchor_scores),
                    page["score"],
                )
                self.assertEqual(
                    expected_view_state,
                    page["viewState"]["state"]["id"],
                )
                self.assertAlmostEqual(
                    (page["score"] + page["viewState"]["state"]["score"]) / 2,
                    page["rankingScore"],
                    places=6,
                )
                elements = {
                    element["id"].removeprefix("heroSelect."): element
                    for element in recognition["elements"]
                    if element["id"].removeprefix("heroSelect.") in all_heroes
                }
                self.assertEqual(all_heroes, set(elements))
                for hero, expected_action in expected_actions.items():
                    element = elements[hero]
                    self.assertEqual("visible", element["visibility"])
                    self.assertTrue(element["visible"])
                    self.assertEqual(
                        {"x": expected_action[0], "y": expected_action[1]},
                        element["actionPointClient"],
                    )
                for hero in all_heroes - set(expected_actions):
                    element = elements[hero]
                    self.assertEqual("viewStateUnknown", element["visibility"])
                    self.assertIsNone(element["visible"])
                    self.assertIsNone(element["actionPointClient"])

    def test_real_choice_frame_distinguishes_choose_from_selected(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "hero-select-choice.zh-CN.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("heroSelect", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.97)
        choose = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "heroSelect.choose"
        )
        selected = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "heroSelect.selected"
        )
        self.assertEqual("visible", choose["visibility"])
        self.assertEqual("notVisible", selected["visibility"])

    def test_detector_only_hero_portrait_does_not_control_page_match(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(SAMPLE_ROOT / "hero-select.zh-CN.holdout.png") as source:
                modified = source.convert("RGB")
                modified.paste((0, 0, 0), (50, 160, 150, 280))
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "hero-select-with-hidden-quincy",
                modified,
            )
            evidence = read_evidence(metadata_path)

            result, match = recognize_image(evidence, self.catalog)

            self.assertIsNotNone(match)
            self.assertEqual("matched", result["recognition"]["status"])
            self.assertEqual("heroSelect", result["recognition"]["page"]["id"])
            self.assertGreater(result["recognition"]["page"]["score"], 0.99)
            self.assertEqual(0.5, result["recognition"]["page"]["rankingScore"])
            self.assertEqual(
                "unknown",
                result["recognition"]["page"]["viewState"]["status"],
            )
            quincy = next(
                element
                for element in result["recognition"]["elements"]
                if element["id"] == "heroSelect.Quincy"
            )
            portrait = next(
                anchor
                for anchor in result["recognition"]["page"]["anchors"]
                if anchor["id"] == "heroSelect.QuincyPortrait"
            )
            self.assertEqual("viewStateUnknown", quincy["visibility"])
            self.assertIsNone(quincy["visible"])
            self.assertFalse(portrait["pageAnchor"])

    def test_real_holdout_frame_matches_stage_settings(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "stage-settings.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("stageSettings", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(6, len(result["recognition"]["elements"]))
        self.assertTrue(result["recognition"]["oracleEligible"])
        continue_button = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "stageSettings.continue"
        )
        in_level = next(
            candidate
            for candidate in result["recognition"]["candidates"]
            if candidate["id"] == "inLevel"
        )
        self.assertEqual("visible", continue_button["visibility"])
        self.assertTrue(continue_button["visible"])
        self.assertFalse(in_level["matched"])

    def test_real_holdout_frame_matches_settings_and_reports_disabled_jukebox(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "settings.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("settings", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(18, len(result["recognition"]["elements"]))
        jukebox_enabled = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "settings.jukeboxEnabled"
        )
        self.assertEqual("notVisible", jukebox_enabled["visibility"])
        self.assertFalse(jukebox_enabled["visible"])

    def test_real_holdout_frame_matches_hotkeys_with_changed_cursor_state(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "hotkeys.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("hotkeys", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(7, len(result["recognition"]["elements"]))
        normal_cursor = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "hotkeys.normalCursorSelected"
        )
        self.assertEqual("notVisible", normal_cursor["visibility"])

    def test_real_holdout_frame_matches_accessibility_with_changed_toggle(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "accessibility.zh-CN.holdout.json")

        result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        self.assertEqual("matched", result["recognition"]["status"])
        self.assertEqual("accessibility", result["recognition"]["page"]["id"])
        self.assertGreater(result["recognition"]["page"]["score"], 0.99)
        self.assertEqual(11, len(result["recognition"]["elements"]))
        map_effects = next(
            element
            for element in result["recognition"]["elements"]
            if element["id"] == "accessibility.mapEffects"
        )
        self.assertEqual("notVisible", map_effects["visibility"])
        self.assertFalse(map_effects["visible"])

    def test_real_extras_frames_match_view_state_placements_and_toggle_states(self) -> None:
        scenarios = (
            (
                "extras-top.zh-CN.json",
                "extras.top",
                {
                    "doubleCash": "enabled",
                    "fastTrack": "disabled",
                    "bigBloons": "disabled",
                    "smallBloons": "enabled",
                    "bigMonkeyTowers": "disabled",
                    "smallMonkeyTowers": "disabled",
                    "smallBosses": "disabled",
                },
            ),
            (
                "extras-top.zh-CN.holdout.json",
                "extras.top",
                {
                    "doubleCash": "enabled",
                    "fastTrack": "enabled",
                    "bigBloons": "disabled",
                    "smallBloons": "enabled",
                    "bigMonkeyTowers": "disabled",
                    "smallMonkeyTowers": "disabled",
                    "smallBosses": "disabled",
                },
            ),
            (
                "extras-bottom.zh-CN.json",
                "extras.bottom",
                {
                    "doubleCash": "enabled",
                    "fastTrack": "disabled",
                    "bigBloons": "disabled",
                    "smallBloons": "enabled",
                    "bigMonkeyTowers": "disabled",
                    "smallMonkeyTowers": "disabled",
                    "smallBosses": "disabled",
                },
            ),
            (
                "extras-bottom.zh-CN.holdout.json",
                "extras.bottom",
                {
                    "doubleCash": "enabled",
                    "fastTrack": "disabled",
                    "bigBloons": "enabled",
                    "smallBloons": "enabled",
                    "bigMonkeyTowers": "disabled",
                    "smallMonkeyTowers": "disabled",
                    "smallBosses": "disabled",
                },
            ),
        )

        for evidence_name, expected_view_state, expected_states in scenarios:
            with self.subTest(evidence=evidence_name):
                evidence = read_evidence(SAMPLE_ROOT / evidence_name)

                result, match = recognize_image(evidence, self.catalog)

                self.assertIsNotNone(match)
                recognition = result["recognition"]
                self.assertEqual("matched", recognition["status"])
                self.assertTrue(recognition["oracleEligible"])
                self.assertEqual("extras", recognition["page"]["id"])
                view_state = recognition["page"]["viewState"]
                self.assertEqual("matched", view_state["status"])
                self.assertTrue(view_state["oracleEligible"])
                self.assertEqual(expected_view_state, view_state["state"]["id"])
                elements = {
                    element["id"]: element for element in recognition["elements"]
                }
                self.assertEqual(9, len(elements))
                for element_name, expected_state in expected_states.items():
                    element = elements[f"extras.{element_name}"]
                    self.assertEqual("visible", element["visibility"])
                    self.assertTrue(element["visible"])
                    self.assertEqual(expected_view_state, element["viewStateId"])
                    self.assertIsNotNone(element["boundsReference"])
                    self.assertEqual("matched", element["state"]["status"])
                    self.assertEqual(expected_state, element["state"]["id"])

    def test_unmodeled_extras_scroll_position_keeps_page_but_view_state_is_unknown(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(SAMPLE_ROOT / "extras-top.zh-CN.holdout.png") as source:
                intermediate = source.convert("RGB")
                scrolled_content = intermediate.crop((0, 140, 1920, 1080))
                intermediate.paste(scrolled_content, (0, 105))
                intermediate.paste((0, 0, 0), (0, 1045, 1920, 1080))
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "extras-intermediate",
                intermediate,
            )
            evidence = read_evidence(metadata_path)

            result, match = recognize_image(evidence, self.catalog)

        self.assertIsNotNone(match)
        recognition = result["recognition"]
        self.assertEqual("matched", recognition["status"])
        self.assertEqual("extras", recognition["page"]["id"])
        view_state = recognition["page"]["viewState"]
        self.assertEqual("unknown", view_state["status"])
        self.assertFalse(view_state["oracleEligible"])
        self.assertIsNone(view_state["state"])
        placement_elements = [
            element
            for element in recognition["elements"]
            if element["id"] not in ("extras.back", "extras.options")
        ]
        self.assertEqual(7, len(placement_elements))
        self.assertTrue(
            all(element["visibility"] == "viewStateUnknown" for element in placement_elements)
        )
        self.assertTrue(all(element["boundsReference"] is None for element in placement_elements))

    def test_uniform_dark_frame_is_unknown(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "dark",
                Image.new("RGB", (1920, 1080), (0, 0, 0)),
            )
            evidence = read_evidence(metadata_path)

            result, match = recognize_image(evidence, self.catalog)

            self.assertIsNone(match)
            self.assertEqual("unknown", result["recognition"]["status"])
            self.assertEqual([], result["recognition"]["elements"])
            self.assertFalse(result["recognition"]["oracleEligible"])

    def test_close_runner_up_below_its_threshold_is_ambiguous(self) -> None:
        source_page = next(page for page in self.catalog.pages if page.id == "mainMenu")
        second_page = VisualPage(
            id="duplicatePage",
            kind="page",
            minimum_score=1.0,
            minimum_matched_anchors=source_page.minimum_matched_anchors,
            anchors=source_page.anchors,
            elements=(),
            positive_holdout=source_page.positive_holdout,
        )
        catalog = VisualCatalog(
            id="ambiguous-test",
            version=1,
            path=self.catalog.path,
            reference_width=self.catalog.reference_width,
            reference_height=self.catalog.reference_height,
            pages=(source_page, second_page),
            sha256=self.catalog.sha256,
        )
        evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")

        result, match = recognize_image(evidence, catalog)

        self.assertIsNone(match)
        self.assertEqual("ambiguous", result["recognition"]["status"])
        self.assertFalse(result["recognition"]["oracleEligible"])

    def test_unchecked_element_occlusion_is_not_reported_as_visible(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(SAMPLE_ROOT / "main-menu.zh-CN.holdout.png") as source:
                modified = source.convert("RGB")
                modified.paste((0, 0, 0), (1710, 850, 1920, 1080))
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "occluded-mods",
                modified,
            )
            evidence = read_evidence(metadata_path)

            result, _ = recognize_image(evidence, self.catalog)

            self.assertEqual("matched", result["recognition"]["status"])
            mods = next(
                element
                for element in result["recognition"]["elements"]
                if element["id"] == "mainMenu.mods"
            )
            self.assertEqual("notEvaluated", mods["visibility"])
            self.assertIsNone(mods["visible"])

    def test_recognition_uses_image_bytes_verified_at_evidence_read_time(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            with Image.open(SAMPLE_ROOT / "main-menu.zh-CN.holdout.png") as source:
                metadata_path = write_test_evidence(root, "snapshot", source.convert("RGB"))
            evidence = read_evidence(metadata_path)
            evidence.image_path.write_bytes(b"replaced after verification")

            result, _ = recognize_image(evidence, self.catalog)

            self.assertEqual("matched", result["recognition"]["status"])
            self.assertEqual(evidence.image_sha256, result["evidence"]["image"]["sha256"])

    def test_annotation_cannot_replace_source_evidence(self) -> None:
        evidence = read_evidence(SAMPLE_ROOT / "main-menu.zh-CN.holdout.json")
        _, match = recognize_image(evidence, self.catalog)
        before = evidence.image_path.read_bytes()

        with self.assertRaises(GameDriverError) as context:
            write_annotation(
                evidence.image_path,
                evidence.image_path,
                match,
                overwrite=True,
            )

        self.assertEqual("protectedEvidenceOutput", context.exception.code)
        self.assertEqual(before, evidence.image_path.read_bytes())

    def test_scaled_16_by_9_frame_matches_and_scales_element_coordinates(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            with Image.open(SAMPLE_ROOT / "main-menu.zh-CN.holdout.png") as source:
                scaled = source.convert("RGB").resize((1280, 720), Image.Resampling.LANCZOS)
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "scaled",
                scaled,
            )
            evidence = read_evidence(metadata_path)

            result, _ = recognize_image(evidence, self.catalog)

            self.assertEqual("matched", result["recognition"]["status"])
            start = next(
                element
                for element in result["recognition"]["elements"]
                if element["id"] == "mainMenu.start"
            )
            self.assertEqual({"x": 640, "y": 633}, start["actionPointClient"])

    def test_non_16_by_9_frame_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "wrong-aspect",
                Image.new("RGB", (1000, 700)),
                has_reference_aspect_ratio=False,
            )
            evidence = read_evidence(metadata_path)

            with self.assertRaises(GameDriverError) as context:
                recognize_image(evidence, self.catalog)

            self.assertEqual("unsupportedObservationAspectRatio", context.exception.code)

    def test_image_size_must_match_evidence_geometry(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            metadata_path = write_test_evidence(
                Path(temporary_directory),
                "geometry",
                Image.new("RGB", (1920, 1080)),
            )
            evidence = read_evidence(metadata_path)
            evidence.metadata["coordinateSystem"]["bounds"]["xMaximumExclusive"] = 1280

            with self.assertRaises(GameDriverError) as context:
                recognize_image(evidence, self.catalog)

            self.assertEqual("evidenceGeometryMismatch", context.exception.code)


if __name__ == "__main__":
    unittest.main()
