"""Prepare straight, single-piano scanned pages for the SMT experiment.

Development-only Pillow/numpy tool; not installed with the OCR add-on. Fails
closed when horizontal projection cannot identify complete five-line staves.
It does not deskew photographs, reconstruct notation, or enhance lost detail.
"""

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image
from smt_scan_layout import barlines, detect_layout


def find_staves(gray: np.ndarray) -> tuple[list[list[int]], int]:
    layout = detect_layout(gray)
    return [staff['lines'] for staff in layout['staves']], layout['threshold']


def prepare(source: Path, destination: Path, staff_spacing: float | None = None,
            suppress_faint: bool = False) -> dict:
    if destination.exists():
        raise ValueError(f"Output directory already exists: {destination}")
    image = Image.open(source).convert("RGB")
    gray = np.asarray(image.convert("L"))
    layout = detect_layout(gray)
    staves = [staff['lines'] for staff in layout['staves']]
    threshold = layout['threshold']
    destination.mkdir(parents=True)
    systems = []
    for index in range(0, len(staves), 2):
        upper, lower = staves[index:index + 2]
        spacing = float(np.median(np.concatenate((np.diff(upper), np.diff(lower)))))
        row_pixels = np.flatnonzero((gray[upper + lower] < threshold).sum(axis=0) >= 5)
        if not len(row_pixels):
            raise ValueError("Cannot locate the staff's horizontal extent.")
        # Keep clefs, ledger lines and nearby annotations; drop titles/page margins.
        left = max(0, int(row_pixels[0] - 3 * spacing))
        right = min(image.width, int(row_pixels[-1] + spacing + 1))
        top = max(0, int(upper[0] - 3 * spacing))
        bottom = min(image.height, int(lower[-1] + 3 * spacing + 1))
        if index:
            top = max(top, (staves[index - 1][-1] + upper[0]) // 2)
        if index + 2 < len(staves):
            bottom = min(bottom, (lower[-1] + staves[index + 2][0]) // 2)
        crop = image.crop((left, top, right, bottom))
        path = destination / f"system-{index // 2 + 1:02d}.png"
        if suppress_faint:
            pixels = np.asarray(crop.convert("L")).copy()
            pixels[pixels > 210] = 255
            crop = Image.fromarray(pixels)
        scale = staff_spacing / spacing if staff_spacing else 256 / crop.height
        resized = crop.resize((max(1, round(crop.width * scale)), max(1, round(crop.height * scale))), Image.Resampling.BILINEAR)
        canvas = Image.new(resized.mode, (resized.width, max(256, resized.height)), 255 if resized.mode == "L" else (255, 255, 255))
        canvas.paste(resized, (0, 0))
        canvas.save(path)
        boundaries = barlines(gray, threshold, upper, lower, left, right)
        systems.append({"file": path.name, "crop": [left, top, right, bottom], "staffLines": upper + lower,
                        "processedSize": list(canvas.size), "barlines": boundaries,
                        "expectedMeasures": len(boundaries) - 1 if len(boundaries) >= 2 else None})
    page = image.resize((round(image.width * 1485 / image.height), 1485), Image.Resampling.BILINEAR)
    page.save(destination / "page-1485.png")
    metadata = {
        "source": str(source.resolve()),
        "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "sourceSize": list(image.size),
        "threshold": threshold,
        "layoutMethod": "paired-five-line-geometry-with-vertical-connections",
        "coverageAudit": layout['coverageAudit'],
        "horizontalCoverage": layout['coverage'],
        "inferredLines": layout['inferredLines'],
        "ignoredLineBands": layout['ignoredBands'],
        "minimumSystemHeight": 256,
        "targetStaffSpacing": staff_spacing,
        "suppressAboveLuminance": 210 if suppress_faint else None,
        "pageHeight": 1485,
        "systems": systems,
        "scope": "Straight single-piano scans only; no notation accuracy validation.",
    }
    (destination / "preparation.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return metadata


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("output_directory", type=Path)
    parser.add_argument("--staff-spacing", type=float, help="Normalize staff spacing instead of filling a 256px-high image.")
    parser.add_argument("--suppress-faint", action="store_true", help="Experimental: turn luminance >210 white before system resizing.")
    args = parser.parse_args()
    if args.staff_spacing is not None and not 4 <= args.staff_spacing <= 20:
        parser.error("--staff-spacing must be between 4 and 20 pixels")
    print(json.dumps(prepare(args.image, args.output_directory, args.staff_spacing, args.suppress_faint), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
