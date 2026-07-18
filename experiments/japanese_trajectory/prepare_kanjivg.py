"""Convert KanjiVG SVG stroke paths into normalized point-sequence JSONL."""

from __future__ import annotations

import argparse
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np
from svgpathtools import parse_path


ROOT = Path(__file__).resolve().parent
DEFAULT_INPUT = ROOT / "data" / "raw" / "kanjivg" / "kanjivg-master" / "kanji"
DEFAULT_OUTPUT = ROOT / "data" / "processed" / "kanjivg-trajectories.jsonl"
STROKE_ID = re.compile(r"-s(\d+)$")


def sample_path(path_data: str, points_per_stroke: int) -> list[list[float]]:
    path = parse_path(path_data)
    if not path:
        return []
    ts = np.linspace(0.0, 1.0, points_per_stroke)
    points = [[float(path.point(float(t)).real), float(path.point(float(t)).imag), 0.0] for t in ts]
    points[-1][2] = 1.0
    return points


def character_from_filename(svg_file: Path) -> str | None:
    try:
        codepoint = int(svg_file.stem.split("-")[0], 16)
        return chr(codepoint)
    except (ValueError, OverflowError):
        return None


def read_svg(svg_file: Path, points_per_stroke: int) -> dict | None:
    character = character_from_filename(svg_file)
    if character is None:
        return None
    root = ET.parse(svg_file).getroot()
    ordered: list[tuple[int, list[list[float]]]] = []
    for element in root.iter():
        if not element.tag.endswith("path") or "d" not in element.attrib:
            continue
        element_id = element.attrib.get("id", "")
        match = STROKE_ID.search(element_id)
        if not match:
            continue
        points = sample_path(element.attrib["d"], points_per_stroke)
        if points:
            ordered.append((int(match.group(1)), points))
    ordered.sort(key=lambda item: item[0])
    if not ordered:
        return None

    flat = np.asarray([point for _, stroke in ordered for point in stroke], dtype=np.float32)
    minimum = flat[:, :2].min(axis=0)
    extent = np.maximum(flat[:, :2].max(axis=0) - minimum, 1e-6)
    scale = float(max(extent))
    center_offset = (scale - extent) / 2.0

    strokes: list[list[list[float]]] = []
    for _, stroke in ordered:
        values = np.asarray(stroke, dtype=np.float32)
        values[:, :2] = (values[:, :2] - minimum + center_offset) / scale
        strokes.append(values.round(6).tolist())
    return {"character": character, "source": "kanjivg", "strokes": strokes}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--points-per-stroke", type=int, default=32)
    args = parser.parse_args()
    if args.points_per_stroke < 4:
        parser.error("--points-per-stroke must be at least 4")
    if not args.input.is_dir():
        parser.error(f"KanjiVG directory not found: {args.input}")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    written = 0
    skipped = 0
    with args.output.open("w", encoding="utf-8", newline="\n") as destination:
        for svg_file in sorted(args.input.glob("*.svg")):
            try:
                item = read_svg(svg_file, args.points_per_stroke)
            except (ET.ParseError, ValueError, ZeroDivisionError):
                item = None
            if item is None:
                skipped += 1
                continue
            destination.write(json.dumps(item, ensure_ascii=False, separators=(",", ":")) + "\n")
            written += 1
    print(f"wrote={written} skipped={skipped} output={args.output}")


if __name__ == "__main__":
    main()
