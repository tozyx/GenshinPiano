"""Inspect SMT trial output; this development tool is not part of inference.

Normalizes both whole-symbol ekern and character-token beKern. This is not
a score importer: it checks structure and compares notation, not note accuracy.
"""

import argparse
import json
import re
from pathlib import Path


def normalize(text: str) -> list[list[str]]:
    text = text.strip().replace("\\t", "\t").replace("\\n", "\n")
    serialized = "<b>" in text or "<t>" in text
    text = text.replace("·", "")
    records = text.split("<b>") if serialized else text.splitlines()
    rows = []
    for record in records:
        if not record.strip():
            continue
        fields = record.split("<t>") if serialized else record.split("\t")
        if serialized:
            # Spaces separate model tokens; <s> separates simultaneous chord notes.
            fields = [" ".join("".join(atom.split()) for atom in field.split("<s>")) for field in fields]
        rows.append([field.strip() for field in fields])
    return rows


def inspect(rows: list[list[str]]) -> dict:
    errors = []
    if not rows:
        errors.append("No notation records.")
    columns = len(rows[0]) if rows else 0
    notes = rests = nulls = data_fields = bars = 0
    terminated = False
    for number, row in enumerate(rows, 1):
        if row[0].startswith("!!"):
            continue
        if len(row) != columns:
            errors.append(f"Record {number}: expected {columns} spines, got {len(row)}.")
        if any(not field for field in row):
            errors.append(f"Record {number}: empty spine field.")
        if row[0].startswith("="):
            bars += 1
            continue
        if row[0].startswith("*"):
            next_columns = columns
            index = 0
            while index < len(row):
                field = row[index]
                if field == "*v":
                    end = index + 1
                    while end < len(row) and row[end] == "*v":
                        end += 1
                    if end - index < 2:
                        errors.append(f"Record {number}: unpaired spine merge.")
                    next_columns -= end - index - 1
                    index = end
                    continue
                if field in ("*^", "*+"):
                    next_columns += 1
                elif field == "*-":
                    next_columns -= 1
                index += 1
            columns = next_columns
            terminated = all(field == "*-" for field in row)
            continue
        if row[0].startswith("!"):
            continue
        if all(field == "." for field in row):
            errors.append(f"Record {number}: all spines are null; no timed event.")
        for field in row:
            data_fields += 1
            nulls += field == "."
            for atom in field.split():
                if re.search(r"\d+\.*[A-Ga-g]", atom):
                    notes += 1
                elif re.search(r"\d+\.*r", atom):
                    rests += 1
    if rows and not terminated:
        errors.append("Missing final spine termination; output may be truncated.")
    return {
        "records": len(rows),
        "pitchedSymbols": notes,
        "restSymbols": rests,
        "barlineRecords": bars,
        "nullFieldFraction": round(nulls / max(1, data_fields), 4),
        "terminated": terminated,
        "structuralErrorCount": len(errors),
        "structuralErrors": errors[:20],
    }


def edit_distance(left: str, right: str) -> int:
    previous = list(range(len(right) + 1))
    for i, a in enumerate(left, 1):
        current = [i]
        for j, b in enumerate(right, 1):
            current.append(min(current[-1] + 1, previous[j] + 1, previous[j - 1] + (a != b)))
        previous = current
    return previous[-1]


def canonical(rows: list[list[str]]) -> str:
    return "\n".join("\t".join(row) for row in rows)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    rows = normalize(args.output.read_text(encoding="utf-8-sig"))
    text = canonical(rows)
    result = inspect(rows)
    normalized_path = args.output.with_suffix(".normalized.krn")
    normalized_path.write_text(text + "\n", encoding="utf-8")
    result["normalizedPath"] = str(normalized_path)
    if args.reference:
        reference_rows = normalize(args.reference.read_text(encoding="utf-8-sig"))
        reference = canonical(reference_rows)
        distance = edit_distance(text, reference)
        result["reference"] = inspect(reference_rows)
        result["normalizedCharacterEdits"] = distance
        result["referenceCharacters"] = len(reference)
        result["normalizedCharacterErrorRate"] = round(distance / max(1, len(reference)), 6)
        result["comparisonScope"] = "Notation character differences, not note or rhythm accuracy."
    analysis_path = args.output.with_suffix(".analysis.json")
    analysis_path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
