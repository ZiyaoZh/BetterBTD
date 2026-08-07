from __future__ import annotations

import hashlib
import struct
from io import BytesIO

from PIL import Image, ImageStat


def encode_bgrx32(width: int, height: int, pixels: bytes) -> bytes:
    image = _open_bgrx32(width, height, pixels)
    output = BytesIO()
    image.save(output, format="PNG", compress_level=6, optimize=False)
    return output.getvalue()


def visible_pixel_sha256(width: int, height: int, pixels: bytes) -> str:
    image = _open_bgrx32(width, height, pixels)
    digest = hashlib.sha256()
    digest.update(struct.pack("<II", width, height))
    digest.update(image.tobytes())
    return digest.hexdigest()


def analyze_bgrx32(width: int, height: int, pixels: bytes) -> dict[str, object]:
    image = _open_bgrx32(width, height, pixels)
    extrema = image.getextrema()
    means = ImageStat.Stat(image).mean
    minimum = {"r": extrema[0][0], "g": extrema[1][0], "b": extrema[2][0]}
    maximum = {"r": extrema[0][1], "g": extrema[1][1], "b": extrema[2][1]}
    mean = {"r": round(means[0], 3), "g": round(means[1], 3), "b": round(means[2], 3)}
    is_uniform = all(minimum[channel] == maximum[channel] for channel in ("r", "g", "b"))
    is_near_black = all(maximum[channel] <= 2 for channel in ("r", "g", "b"))

    return {
        "pixelCount": width * height,
        "minimumRgb": minimum,
        "maximumRgb": maximum,
        "meanRgb": mean,
        "isUniform": is_uniform,
        "isNearBlack": is_near_black,
    }


def _open_bgrx32(width: int, height: int, pixels: bytes) -> Image.Image:
    _validate_pixels(width, height, pixels)
    return Image.frombytes("RGB", (width, height), pixels, "raw", "BGRX")


def _validate_pixels(width: int, height: int, pixels: bytes) -> None:
    if width <= 0 or height <= 0:
        raise ValueError("Image dimensions must be positive.")
    expected_length = width * height * 4
    if len(pixels) != expected_length:
        raise ValueError(
            f"Expected {expected_length} BGRX bytes for {width}x{height}, got {len(pixels)}."
        )
