from __future__ import annotations

from .models import Rect


REFERENCE_WIDTH = 1920
REFERENCE_HEIGHT = 1080
ASPECT_RATIO_TOLERANCE = 0.01


def reference_to_client(
    x: float,
    y: float,
    actual_width: int,
    actual_height: int,
) -> tuple[int, int]:
    return (
        scale_reference_coordinate(x, REFERENCE_WIDTH, actual_width),
        scale_reference_coordinate(y, REFERENCE_HEIGHT, actual_height),
    )


def client_to_reference(
    x: float,
    y: float,
    actual_width: int,
    actual_height: int,
) -> tuple[float, float]:
    _validate_actual_size(actual_width)
    _validate_actual_size(actual_height)
    return (
        x / actual_width * REFERENCE_WIDTH,
        y / actual_height * REFERENCE_HEIGHT,
    )


def client_to_screen(x: float, y: float, client_rect_on_screen: Rect) -> tuple[float, float]:
    return client_rect_on_screen.x + x, client_rect_on_screen.y + y


def screen_to_client(x: float, y: float, client_rect_on_screen: Rect) -> tuple[float, float]:
    return x - client_rect_on_screen.x, y - client_rect_on_screen.y


def reference_rect_to_client(reference_rect: Rect, actual_width: int, actual_height: int) -> Rect:
    _validate_actual_size(actual_width)
    _validate_actual_size(actual_height)
    left = round(reference_rect.x / REFERENCE_WIDTH * actual_width)
    top = round(reference_rect.y / REFERENCE_HEIGHT * actual_height)
    right = round(reference_rect.right / REFERENCE_WIDTH * actual_width)
    bottom = round(reference_rect.bottom / REFERENCE_HEIGHT * actual_height)

    left = _clamp(left, 0, max(0, actual_width - 1))
    top = _clamp(top, 0, max(0, actual_height - 1))
    right = _clamp(right, left + 1, actual_width)
    bottom = _clamp(bottom, top + 1, actual_height)
    return Rect(left, top, right - left, bottom - top)


def scale_reference_coordinate(
    coordinate: float,
    reference_size: int,
    actual_size: int,
) -> int:
    if reference_size <= 0:
        raise ValueError("Reference size must be positive.")
    _validate_actual_size(actual_size)
    scaled = round(coordinate / reference_size * actual_size)
    return _clamp(scaled, 0, max(0, actual_size - 1))


def has_reference_aspect_ratio(
    actual_width: int,
    actual_height: int,
    tolerance: float = ASPECT_RATIO_TOLERANCE,
) -> bool:
    if actual_width <= 0 or actual_height <= 0:
        return False
    reference_ratio = REFERENCE_WIDTH / REFERENCE_HEIGHT
    actual_ratio = actual_width / actual_height
    return abs(actual_ratio - reference_ratio) <= tolerance


def coordinate_metadata(actual_width: int, actual_height: int) -> dict[str, object]:
    _validate_actual_size(actual_width)
    _validate_actual_size(actual_height)
    return {
        "referenceSpace": {
            "id": "btd6Reference1920x1080",
            "width": REFERENCE_WIDTH,
            "height": REFERENCE_HEIGHT,
        },
        "referenceToClientScale": {
            "x": actual_width / REFERENCE_WIDTH,
            "y": actual_height / REFERENCE_HEIGHT,
        },
        "hasReferenceAspectRatio": has_reference_aspect_ratio(actual_width, actual_height),
        "pointFormula": "round(reference / referenceSize * actualSize), clamped to [0, actualSize)",
        "rectangleConvention": "halfOpen",
    }


def _validate_actual_size(actual_size: int) -> None:
    if actual_size <= 0:
        raise ValueError("Actual size must be positive.")


def _clamp(value: int, minimum: int, maximum: int) -> int:
    return min(max(value, minimum), maximum)
