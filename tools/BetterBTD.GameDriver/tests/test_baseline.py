import unittest
from unittest.mock import patch
from dataclasses import replace
from io import BytesIO

from PIL import Image

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
        number_writes = [
            call
            for call in write_template.call_args_list
            if "templates\\numbers" in str(call.args[0])
        ]
        self.assertEqual(10, len(number_writes))
        for write in number_writes:
            with Image.open(BytesIO(write.args[1])) as template:
                self.assertEqual("RGBA", template.mode)
                pixels = list(template.get_flattened_data())
            self.assertEqual({0, 255}, {alpha for *_, alpha in pixels})
            self.assertTrue(
                all(
                    pixel in ((0, 0, 0, 0), (255, 255, 255, 255))
                    for pixel in pixels
                )
            )

    def test_schema_v3_rgb_number_templates_rebuild_with_legacy_hashes(self) -> None:
        legacy_hashes = (
            "0cbfe17415ed69cc0a55ebb07c04f2a064ba0142bfec98e646565f849b1707b0",
            "546fd60d8740cc5854a168ebe43d0052b4debe45e7acebbc159f142cd8857e89",
            "11330e62f0e2bc8335dd4eb78aae5a300fba35c19f7d13487e14b9075d006e5b",
            "7fbf341ecfa74cb3ef737265e6c7e32e550932e93413ffecd4598378cf88caf3",
            "41b0c9701857339c8fb817de513a22f9571ae9b1dd406fd0361203c99dd5732b",
            "15792e7be9f4cb63f387e295356ff518df13ae8a442f13df558b7ee7f639b5df",
            "a62a1cc83bc38663e94406862554389fba9e772f60d3b7244319551b93c808f1",
            "b10c9e3e2006122bd2936899a78414af4b34e5d997a6346223143cfc0c4a02d8",
            "f81d4de71aa967098fba44eff8d84c809a91146cec4902657ee8a3a47543c83f",
            "cb5155267a1f7f1cc5183658a1da2a652a0549b4d3885a4b8bf4ce85ac95bf35",
        )
        catalog = load_visual_catalog()
        model = catalog.number_models[0]
        legacy_model = replace(
            model,
            glyphs=tuple(
                replace(glyph, template_sha256=legacy_hashes[glyph.digit])
                for glyph in model.glyphs
            ),
            uses_binary_alpha_mask=False,
        )
        legacy_catalog = replace(
            catalog,
            schema_version=3,
            number_models=(legacy_model,),
        )

        with patch(
            "betterbtd_game_driver.baseline._write_template"
        ) as write_template:
            build_templates(legacy_catalog, overwrite=True)

        number_writes = [
            call
            for call in write_template.call_args_list
            if "templates\\numbers" in str(call.args[0])
        ]
        self.assertEqual(10, len(number_writes))
        for write in number_writes:
            with Image.open(BytesIO(write.args[1])) as template:
                self.assertEqual("RGB", template.mode)

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
