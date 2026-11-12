"""Reference-free diagnostics and conservative retry ranking for SMT scans."""

from collections import Counter
from fractions import Fraction

from analyze_smt_output import inspect, normalize
from smt_notation import read_smt


def assess(path, expected_measures=None, beat_budget=None, allowed_short_measures=None):
    try:
        rows = normalize(path.read_text(encoding='utf-8-sig'))
        structure = inspect(rows)
        events, details = read_smt([path])
        if not events or not structure['terminated']:
            raise ValueError('Empty or unterminated notation.')
    except (ValueError, OSError, IndexError, ZeroDivisionError) as error:
        return dict(score=10000, retry=True, fatal=str(error), issues=['invalid_notation'])

    lengths = [row[staff] for row in details['staffDurations'] for staff in ('lower', 'upper') if row[staff] > 0]
    counts = Counter(lengths)
    modal, support = counts.most_common(1)[0]
    # A rhythmic consensus is evidence, not a forced 4/4 default.
    budget = Fraction(beat_budget) if beat_budget is not None else (modal if support >= 3 and support > len(lengths) / 2 else None)
    changes = {row['measure']: row['value'] for row in details['meters']}
    meter_conflicts = duration_errors = 0
    duration_distance = Fraction()
    declared = None
    declared_at = None
    conflict_locations = set()
    allowed_shorts = (None if beat_budget is None else set()) if allowed_short_measures is None else set(allowed_short_measures)
    short_measures = []
    unverified = 0
    suspect_measures = set()
    for row in details['staffDurations']:
        if row['measure'] in changes:
            declared_at = row['measure']
            try:
                num, den = changes[row['measure']].split('/')
                declared = Fraction(int(num) * 4, int(den))
            except (ValueError, ZeroDivisionError):
                declared = None
        actual = [row['lower'], row['upper']]
        target = declared if declared and all(value == declared for value in actual) else budget
        if target is None:
            unverified += 1
            suspect_measures.add(row['measure'])
            continue
        if declared and declared != target:
            conflict_locations.add(declared_at)
            suspect_measures.add(declared_at)
        # A balanced short first/final measure can be a legitimate pickup.
        edge = row['measure'] in (1, details['measures'])
        if (edge and actual[0] == actual[1] and 0 < actual[0] < target
                and (allowed_shorts is None or row['measure'] in allowed_shorts)):
            short_measures.append(row['measure'])
            continue
        for value in actual:
            if value != target:
                duration_errors += 1
                suspect_measures.add(row['measure'])
                duration_distance += abs(value-target)

    pitched = [event for event in events if event['pitch'] is not None]
    signatures = Counter((e['measure'], e['staff'], e['start'], e['duration'], e['pitch']) for e in pitched)
    duplicates = sum(count-1 for count in signatures.values())
    suspect_measures.update(key[0] for key,count in signatures.items() if count > 1)
    active = {}
    tie_errors = 0
    for event in sorted(pitched, key=lambda e:(e['measure'], e['start'])):
        key = (event['staff'], event['voice'], event['pitch'])
        if event['tieStop']:
            if key not in active:
                tie_errors += 1
                suspect_measures.add(event['measure'])
            active.pop(key, None)
        if event['tieStart']:
            if key in active:
                tie_errors += 1
                suspect_measures.add(event['measure'])
            active[key] = event
    tie_errors += len(active)
    suspect_measures.update(event['measure'] for event in active.values())
    null_rows = sum(all(field == '.' for field in row) for row in rows)
    other_structure = max(0, structure['structuralErrorCount'] - null_rows)
    count_error = abs(details['measures'] - expected_measures) if expected_measures is not None else 0
    if count_error:
        # A missing boundary makes every following measure index uncertain.
        suspect_measures.update(range(1,max(details['measures'],expected_measures)+1))
    meter_conflicts = len(conflict_locations)
    score = (count_error*1000 + other_structure*100 + duration_errors*20 + float(duration_distance)*10
             + meter_conflicts*15 + tie_errors*10 + duplicates*10 + unverified*25)
    issues = []
    for name, count in [('measure_count',count_error), ('structure',other_structure),
                        ('staff_duration',duration_errors), ('meter_conflict',meter_conflicts),
                        ('tie',tie_errors), ('suspected_duplicate',duplicates), ('rhythm_unverified',unverified)]:
        if count:
            issues.append(name)
    return dict(score=score, retry=bool(issues), issues=issues, expectedMeasures=expected_measures,
                measures=details['measures'], inferredQuarterBeatBudget=str(budget) if budget else None,
                durationErrors=duration_errors, durationDistance=str(duration_distance),
                meterConflicts=meter_conflicts, tieErrors=tie_errors, suspectedDuplicates=duplicates,
                suspectMeasures=sorted(suspect_measures), unverifiedMeasures=unverified,
                allowedShortMeasures=short_measures,
                ignoredNullRows=null_rows, pitchedSymbols=len(pitched), staffDurations=details['staffDurations'])


def choose(candidates):
    """Stable tie-breaking retains baseline; do not replace on equal evidence."""
    return min((candidate for candidate in candidates if not candidate.get('rejected')),
               key=lambda candidate:candidate['quality']['score'])


def changes_unflagged_measures(baseline_path, candidate_path, baseline_quality):
    """Protect baseline content where no anomaly supplied a reason to change it."""
    before, _ = read_smt([baseline_path])
    after, _ = read_smt([candidate_path])
    flagged = set(baseline_quality.get('suspectMeasures', []))
    def signatures(events):
        return Counter((e['measure'],e['staff'],e['start'],e['duration'],e['pitch'])
                       for e in events if e['measure'] not in flagged)
    return signatures(before) != signatures(after)
