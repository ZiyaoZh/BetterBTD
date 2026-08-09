import unittest
from unittest.mock import patch
from dataclasses import replace

from betterbtd_game_driver.baseline import build_templates
from betterbtd_game_driver.errors import GameDriverError
from betterbtd_game_driver.visual_catalog import load_visual_catalog


class BaselineBuildTests(unittest.TestCase):
    def test_bundled_templates_rebuild_with_declared_provenance_and_hashes(self) -> None:
        catalog = load_visual_catalog()

        with patch("betterbtd_game_driver.baseline._write_template") as write_template:
            result = build_templates(catalog, overwrite=True)

        self.assertEqual(421, len(result["templates"]))
        self.assertEqual(421, write_template.call_count)
        number_templates = [
            template
            for template in result["templates"]
            if template.get("numberModelId") == "btd6HudWhiteDigits"
        ]
        self.assertEqual(list(range(10)), [item["digit"] for item in number_templates])

    def test_late_validation_failure_writes_no_templates(self) -> None:
        catalog = load_visual_catalog()
        model = catalog.number_models[-1]
        invalid_glyph = replace(model.glyphs[-1], template_sha256="0" * 64)
        invalid_model = replace(model, glyphs=(*model.glyphs[:-1], invalid_glyph))
        invalid_catalog = replace(
            catalog,
            number_models=(*catalog.number_models[:-1], invalid_model),
        )

        with patch("betterbtd_game_driver.baseline._write_template") as write_template:
            with self.assertRaises(GameDriverError) as context:
                build_templates(invalid_catalog, overwrite=True)

        self.assertEqual("baselineTemplateMismatch", context.exception.code)
        write_template.assert_not_called()


if __name__ == "__main__":
    unittest.main()
