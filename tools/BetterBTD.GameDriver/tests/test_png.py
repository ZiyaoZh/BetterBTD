import hashlib
from io import BytesIO
import struct
import unittest

from PIL import Image

from betterbtd_game_driver.png import (
    analyze_bgrx32,
    encode_bgrx32,
    visible_pixel_sha256,
)


class PngEvidenceTests(unittest.TestCase):
    def test_bgrx_pixels_are_encoded_as_rgb_png(self) -> None:
        pixels = bytes(
            (
                0, 0, 255, 0,
                0, 255, 0, 0,
            )
        )

        encoded = encode_bgrx32(2, 1, pixels)

        with Image.open(BytesIO(encoded)) as image:
            self.assertEqual("PNG", image.format)
            self.assertEqual((2, 1), image.size)
            self.assertEqual((255, 0, 0), image.getpixel((0, 0)))
            self.assertEqual((0, 255, 0), image.getpixel((1, 0)))

    def test_pixel_fingerprint_includes_dimensions_and_normalized_rgb(self) -> None:
        pixels = bytes((1, 2, 3, 4))
        expected = hashlib.sha256(struct.pack("<II", 1, 1) + bytes((3, 2, 1))).hexdigest()

        self.assertEqual(expected, visible_pixel_sha256(1, 1, pixels))

    def test_frame_analysis_identifies_uniform_black_frame(self) -> None:
        analysis = analyze_bgrx32(2, 2, bytes(2 * 2 * 4))

        self.assertTrue(analysis["isUniform"])
        self.assertTrue(analysis["isNearBlack"])
        self.assertEqual({"r": 0, "g": 0, "b": 0}, analysis["maximumRgb"])

    def test_invalid_pixel_length_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "Expected 8 BGRX bytes"):
            encode_bgrx32(2, 1, bytes(4))


if __name__ == "__main__":
    unittest.main()
