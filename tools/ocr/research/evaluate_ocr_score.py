"""Compare a numbered-notation OCR result with a hand-authored .gpiano file.

The reference may contain melody and accompaniment in one track. At each
startTick, its first note is treated as melody and the optional second note as
accompaniment, matching the ordering used by the legacy Lemon score.
"""

from __future__ import annotations

import argparse
import json
from collections import OrderedDict
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass
class Alignment:
    distance: int
    substitutions: list[tuple[dict[str, Any], dict[str, Any]]]
    extras: list[dict[str, Any]]
    missing: list[dict[str, Any]]
    pairs: list[tuple[dict[str, Any], dict[str, Any]]]


def load_score(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8-sig"))
    return document.get("score", document)


def split_reference(score: dict[str, Any]) -> list[list[dict[str, Any]]]:
    grouped: OrderedDict[int, list[dict[str, Any]]] = OrderedDict()
    for note in score["tracks"][0]["notes"]:
        grouped.setdefault(note["startTick"], []).append(note)
    melody = [notes[0] for notes in grouped.values()]
    accompaniment = [note for notes in grouped.values() for note in notes[1:]]
    return [melody, accompaniment]


def align(reference: list[dict[str, Any]], actual: list[dict[str, Any]]) -> Alignment:
    rows = len(reference) + 1
    columns = len(actual) + 1
    costs = [[0] * columns for _ in range(rows)]
    for row in range(rows):
        costs[row][0] = row
    for column in range(columns):
        costs[0][column] = column
    for row in range(1, rows):
        for column in range(1, columns):
            substitution = reference[row - 1]["pitch"] != actual[column - 1]["pitch"]
            costs[row][column] = min(
                costs[row - 1][column] + 1,
                costs[row][column - 1] + 1,
                costs[row - 1][column - 1] + int(substitution),
            )

    row = len(reference)
    column = len(actual)
    substitutions: list[tuple[dict[str, Any], dict[str, Any]]] = []
    extras: list[dict[str, Any]] = []
    missing: list[dict[str, Any]] = []
    pairs: list[tuple[dict[str, Any], dict[str, Any]]] = []
    while row or column:
        if row and column:
            mismatch = reference[row - 1]["pitch"] != actual[column - 1]["pitch"]
            if costs[row][column] == costs[row - 1][column - 1] + int(mismatch):
                pair = (reference[row - 1], actual[column - 1])
                pairs.append(pair)
                if mismatch:
                    substitutions.append(pair)
                row -= 1
                column -= 1
                continue
        if column and costs[row][column] == costs[row][column - 1] + 1:
            extras.append(actual[column - 1])
            column -= 1
        else:
            missing.append(reference[row - 1])
            row -= 1

    return Alignment(
        costs[-1][-1],
        list(reversed(substitutions)),
        list(reversed(extras)),
        list(reversed(missing)),
        list(reversed(pairs)),
    )


def note_summary(note: dict[str, Any]) -> str:
    return (
        f"pitch={note['pitch']} tick={note['startTick']} "
        f"rhythm={note.get('rhythmTick', note.get('durationTick'))}"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("reference", type=Path)
    parser.add_argument("ocr_result", type=Path)
    args = parser.parse_args()

    reference_voices = split_reference(load_score(args.reference))
    actual_tracks = load_score(args.ocr_result)["tracks"]
    labels = ["melody", "accompaniment"]
    for index, reference in enumerate(reference_voices):
        if index >= len(actual_tracks):
            print(f"{labels[index]}: OCR track missing")
            continue
        actual = actual_tracks[index]["notes"]
        result = align(reference, actual)
        exact_pitch = sum(a["pitch"] == b["pitch"] for a, b in result.pairs)
        exact_rhythm = sum(
            a["pitch"] == b["pitch"] and a["rhythmTick"] == b["rhythmTick"]
            for a, b in result.pairs
        )
        exact_start = sum(
            a["pitch"] == b["pitch"] and a["startTick"] == b["startTick"]
            for a, b in result.pairs
        )
        pitch_matched_pairs = [
            (a, b) for a, b in result.pairs if a["pitch"] == b["pitch"]
        ]
        mean_start_error = (
            sum(abs(a["startTick"] - b["startTick"]) for a, b in pitch_matched_pairs)
            / len(pitch_matched_pairs)
            if pitch_matched_pairs
            else 0
        )
        print(
            f"{labels[index]}: reference={len(reference)} actual={len(actual)} "
            f"edit={result.distance} substitutions={len(result.substitutions)} "
            f"extras={len(result.extras)} missing={len(result.missing)} "
            f"pitchExact={exact_pitch}/{len(reference)} "
            f"rhythmExact={exact_rhythm}/{len(reference)} "
            f"startExact={exact_start}/{len(reference)} "
            f"meanStartError={mean_start_error:.1f}"
        )
        if result.extras:
            print("  extras:")
            for note in result.extras:
                print(f"    {note_summary(note)}")
        if result.substitutions:
            print("  substitutions (reference -> OCR):")
            for expected, observed in result.substitutions:
                print(f"    {note_summary(expected)} -> {note_summary(observed)}")


if __name__ == "__main__":
    main()
