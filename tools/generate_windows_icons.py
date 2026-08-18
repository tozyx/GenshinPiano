from __future__ import annotations

import math
import shutil
from pathlib import Path

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
MASTER = ROOT / "src/GenshinPiano.App/Assets/Brand/GenshinPiano-master.png"
ICON_DIR = ROOT / "src/GenshinPiano.App/Assets/Icons"
PACKAGE_DIR = ROOT / "src/GenshinPiano.App/Assets/Package"

TARGET_SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
ICO_SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256)
SCALES = (100, 125, 150, 200, 250, 300, 400)


def scaled_size(base: int, scale: int) -> int:
    return math.floor(base * scale / 100 + 0.5)


def resize_icon(source: Image.Image, size: int) -> Image.Image:
    resized = source.resize((size, size), Image.Resampling.LANCZOS)
    if size <= 48:
        resized = resized.filter(ImageFilter.UnsharpMask(radius=0.65, percent=110, threshold=2))
    return resized


def tile_asset(source: Image.Image, width: int, height: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    icon_size = max(1, math.floor(min(width, height) * 0.88))
    icon = resize_icon(source, icon_size)
    canvas.alpha_composite(icon, ((width - icon_size) // 2, (height - icon_size) // 2))
    return canvas


def save_scale_series(source: Image.Image, name: str, base_width: int, base_height: int) -> None:
    for scale in SCALES:
        width = scaled_size(base_width, scale)
        height = scaled_size(base_height, scale)
        tile_asset(source, width, height).save(PACKAGE_DIR / f"{name}.scale-{scale}.png")


def main() -> None:
    ICON_DIR.mkdir(parents=True, exist_ok=True)
    PACKAGE_DIR.mkdir(parents=True, exist_ok=True)

    with Image.open(MASTER) as loaded:
        source = loaded.convert("RGBA")

    resize_icon(source, 512).save(ICON_DIR / "GenshinPiano-512.png")
    resize_icon(source, 256).save(ICON_DIR / "GenshinPiano-256.png")
    resize_icon(source, 256).save(
        ICON_DIR / "GenshinPiano.ico",
        format="ICO",
        sizes=[(size, size) for size in ICO_SIZES],
        bitmap_format="png",
    )

    for size in TARGET_SIZES:
        icon = resize_icon(source, size)
        for logical_name in ("AppList", "Square44x44Logo"):
            icon.save(PACKAGE_DIR / f"{logical_name}.targetsize-{size}.png")
            icon.save(PACKAGE_DIR / f"{logical_name}.targetsize-{size}_altform-unplated.png")
            icon.save(PACKAGE_DIR / f"{logical_name}.targetsize-{size}_altform-lightunplated.png")

    save_scale_series(source, "Square44x44Logo", 44, 44)
    save_scale_series(source, "Square71x71Logo", 71, 71)
    save_scale_series(source, "Square150x150Logo", 150, 150)
    save_scale_series(source, "Square310x310Logo", 310, 310)
    save_scale_series(source, "SmallTile", 71, 71)
    save_scale_series(source, "MedTile", 150, 150)
    save_scale_series(source, "WideTile", 310, 150)
    save_scale_series(source, "LargeTile", 310, 310)
    save_scale_series(source, "StoreLogo", 50, 50)

    shutil.copy2(PACKAGE_DIR / "Square44x44Logo.scale-100.png", PACKAGE_DIR / "Square44x44Logo.png")
    shutil.copy2(PACKAGE_DIR / "Square150x150Logo.scale-100.png", PACKAGE_DIR / "Square150x150Logo.png")
    shutil.copy2(PACKAGE_DIR / "StoreLogo.scale-100.png", PACKAGE_DIR / "StoreLogo.png")


if __name__ == "__main__":
    main()
