"""SMT event reader shared by inference validation and offline scoring.

This module does not load reference scores or choose recognition candidates.
"""

from fractions import Fraction
import re
from analyze_smt_output import normalize


def kern_atom(token):
    match = re.match(r"^(\d+)(\.*)([A-Ga-g]+|r)([#n-]*)", token)
    if not match or int(match[1]) == 0 or 'q' in token:
        raise ValueError(f"Unsupported evaluation token: {token}")
    duration = Fraction(4, int(match[1])) * sum((Fraction(1, 2**i) for i in range(len(match[2]) + 1)), Fraction())
    letters = match[3]
    pitch = None
    if letters != 'r':
        if len(set(letters)) != 1:
            raise ValueError(f"Invalid pitch token: {token}")
        octave = len(letters) + 3 if letters.islower() else 4 - len(letters)
        pitch = 12 * (octave + 1) + dict(C=0, D=2, E=4, F=5, G=7, A=9, B=11)[letters[0].upper()]
        pitch += match[4].count('#') - match[4].count('-')
    return duration, pitch, '[' in token or '_' in token, ']' in token or '_' in token


def event(measure, staff, start, duration, pitch, tie_start=False, tie_stop=False, voice=1):
    return dict(measure=measure, staff=staff, start=start, duration=duration, pitch=pitch,
                tieStart=tie_start, tieStop=tie_stop, voice=voice)


def read_smt(paths):
    events, meters, extents = [], [], []
    measure = 1
    for path in paths:
        spines = [dict(staff=2, voice=1, end=Fraction()), dict(staff=1, voice=2, end=Fraction())]
        next_voice = 3
        clock = Fraction()
        has_data = False
        for row in normalize(path.read_text(encoding='utf-8-sig')):
            if len(row) != len(spines) or any(field in ('*+', '*x') for field in row):
                raise ValueError('Unexpected SMT spine count or unsupported spine exchange/addition.')
            if row[0].startswith('*M'):
                meters.append(dict(measure=measure, value=row[0][2:]))
            if row[0].startswith(('*', '!')):
                changed = []
                index = 0
                while index < len(row):
                    current = spines[index]
                    field = row[index]
                    if field == '*^':
                        changed.append(current)
                        changed.append(dict(current, voice=next_voice))
                        next_voice += 1
                    elif field == '*v':
                        end = index + 1
                        while end < len(row) and row[end] == '*v':
                            end += 1
                        group = spines[index:end]
                        if len(group) < 2 or len({s['staff'] for s in group}) != 1:
                            raise ValueError('Unpaired or cross-staff spine merge.')
                        changed.append(dict(current, end=max(s['end'] for s in group)))
                        index = end
                        continue
                    else:
                        changed.append(current)
                    index += 1
                spines = changed
                continue
            if row[0].startswith('='):
                if has_data:
                    extents.append(dict(measure=measure,
                                        lower=max(s['end'] for s in spines if s['staff'] == 2),
                                        upper=max(s['end'] for s in spines if s['staff'] == 1)))
                    measure += 1
                    clock = Fraction()
                    for spine in spines:
                        spine['end'] = Fraction()
                    has_data = False
                continue
            if all(field == '.' for field in row):
                continue
            for spine, field in enumerate(row):
                if field == '.':
                    continue
                atoms = [kern_atom(token) for token in field.split()]
                current = spines[spine]
                for duration, pitch, begin, end in atoms:
                    events.append(event(measure, current['staff'], clock, duration, pitch, begin, end, current['voice']))
                current['end'] = clock + max(atom[0] for atom in atoms)
                has_data = True
            future = [spine['end'] for spine in spines if spine['end'] > clock]
            if future:
                clock = min(future)
        if has_data:
            raise ValueError(f"Unclosed last measure: {path}")
    return events, dict(meters=meters, measures=measure - 1, staffDurations=extents)


def merge_ties(events):
    result, active = [], {}
    for item in sorted(events, key=lambda e:(e['measure'], e['start'], e['staff'], e['pitch'] or -1)):
        current = dict(item)
        key = (current['staff'], current['voice'], current['pitch'])
        previous = active.get(key)
        absolute_start = (current['measure'] - 1) * 4 + current['start']
        if current['tieStop'] and previous is not None:
            absolute_end = (previous['measure'] - 1) * 4 + previous['start'] + previous['duration']
            if absolute_start != absolute_end:
                raise ValueError('Non-contiguous tie in evaluation subset.')
            previous['duration'] += current['duration']
        else:
            result.append(current)
            previous = current
        if current['tieStart']:
            active[key] = previous
        else:
            active.pop(key, None)
    return result
