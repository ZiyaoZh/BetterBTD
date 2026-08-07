from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class Rect:
    x: int
    y: int
    width: int
    height: int

    @property
    def right(self) -> int:
        return self.x + self.width

    @property
    def bottom(self) -> int:
        return self.y + self.height

    @property
    def area(self) -> int:
        return self.width * self.height

    def contains(self, other: Rect) -> bool:
        return (
            other.x >= self.x
            and other.y >= self.y
            and other.right <= self.right
            and other.bottom <= self.bottom
        )

    def to_dict(self) -> dict[str, int]:
        return {
            "x": self.x,
            "y": self.y,
            "width": self.width,
            "height": self.height,
            "right": self.right,
            "bottom": self.bottom,
        }


@dataclass(frozen=True, slots=True)
class WindowSelector:
    handle: int | None = None
    process_id: int | None = None
    process_names: tuple[str, ...] = ()
    titles: tuple[str, ...] = ()


@dataclass(frozen=True, slots=True)
class WindowSnapshot:
    handle: int
    process_id: int
    process_name: str | None
    title: str
    visible: bool
    minimized: bool
    foreground: bool
    dpi: int
    window_rect: Rect
    client_rect: Rect

    def to_dict(self) -> dict[str, object]:
        return {
            "handle": f"0x{self.handle:016X}",
            "processId": self.process_id,
            "processName": self.process_name,
            "title": self.title,
            "visible": self.visible,
            "minimized": self.minimized,
            "foreground": self.foreground,
            "dpi": self.dpi,
            "scaleFactor": round(self.dpi / 96, 4),
            "windowRectOnScreen": self.window_rect.to_dict(),
            "clientRectOnScreen": self.client_rect.to_dict(),
        }
