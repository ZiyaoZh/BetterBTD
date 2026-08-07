import unittest

from betterbtd_game_driver.coordinates import (
    client_to_reference,
    client_to_screen,
    has_reference_aspect_ratio,
    reference_rect_to_client,
    reference_to_client,
    scale_reference_coordinate,
    screen_to_client,
)
from betterbtd_game_driver.models import Rect


class CoordinateTests(unittest.TestCase):
    def test_reference_center_scales_to_actual_client_center(self) -> None:
        self.assertEqual((480, 270), reference_to_client(960, 540, 960, 540))

    def test_reference_coordinate_is_clamped_to_half_open_client_bounds(self) -> None:
        self.assertEqual(959, scale_reference_coordinate(1920, 1920, 960))
        self.assertEqual(0, scale_reference_coordinate(-1, 1920, 960))

    def test_client_coordinate_converts_back_to_reference_space(self) -> None:
        self.assertEqual((960.0, 540.0), client_to_reference(640, 360, 1280, 720))

    def test_reference_rectangle_scales_edges_before_calculating_size(self) -> None:
        actual = reference_rect_to_client(Rect(100, 100, 300, 200), 960, 540)

        self.assertEqual(Rect(50, 50, 150, 100), actual)

    def test_screen_and_client_coordinates_use_client_origin(self) -> None:
        bounds = Rect(577, 185, 1920, 1080)

        self.assertEqual((677, 385), client_to_screen(100, 200, bounds))
        self.assertEqual((100, 200), screen_to_client(677, 385, bounds))

    def test_aspect_ratio_check_matches_betterbtd_tolerance(self) -> None:
        self.assertTrue(has_reference_aspect_ratio(1920, 1080))
        self.assertFalse(has_reference_aspect_ratio(1600, 1200))

    def test_invalid_actual_size_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "Actual size must be positive"):
            reference_to_client(10, 10, 0, 1080)


if __name__ == "__main__":
    unittest.main()
