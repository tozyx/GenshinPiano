"""Compare manually annotated Rice Field scan measures.

Development evaluation only. SMT supports paired piano staves with adjacent
spine splits/merges, not a general Humdrum importer. Events match within measure
and staff; meter and rest errors are reported separately. Fractions retain
exact written timing. Tied notes are merged into a single sounding event.
"""

import argparse
from collections import Counter
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from analyze_smt_output import edit_distance
from smt_notation import event, kern_atom, merge_ties, read_smt


def read_reference(path):
    reference = json.loads(path.read_text(encoding='utf-8-sig'))
    events = []
    for staff, label in [(1, 'upper'), (2, 'lower')]:
        for measure, text in enumerate(reference[label], 1):
            offset = Fraction()
            for token in text.split():
                atoms = [kern_atom(atom) for atom in token.split('&')]
                if len({atom[0] for atom in atoms}) != 1:
                    raise ValueError('Reference chord durations differ.')
                for duration, pitch, begin, end in atoms:
                    events.append(event(measure, staff, offset, duration, pitch, begin, end))
                offset += atoms[0][0]
            if offset != 4:
                raise ValueError(f"Reference measure {measure}, staff {staff} has {offset} beats.")
    return events


def read_musicxml(path, limit):
    root = ET.parse(path).getroot()
    if root.tag != 'score-partwise' or len(root.findall('part')) != 1:
        raise ValueError('Expected one part with two staves.')
    events, meters, extents = [], [], []
    measures = root.find('part').findall('measure')
    divisions = 1
    for measure, node in enumerate(measures[:limit], 1):
        cursor = extent = Fraction()
        starts = {}
        for item in node:
            if item.tag == 'attributes':
                divisions = int(item.findtext('divisions', str(divisions)))
                time = item.find('time')
                if time is not None:
                    meters.append(dict(measure=measure, value=f"{time.findtext('beats')}/{time.findtext('beat-type')}"))
                continue
            if item.tag in ('backup', 'forward'):
                movement = Fraction(int(item.findtext('duration', '0')), divisions)
                cursor += movement if item.tag == 'forward' else -movement
                if cursor < 0:
                    raise ValueError('MusicXML backup before measure start.')
                extent = max(extent, cursor)
                continue
            if item.tag != 'note':
                continue
            if item.find('grace') is not None:
                raise ValueError('Grace-note timing requires separate evaluation.')
            duration = Fraction(int(item.findtext('duration', '0')), divisions)
            staff = int(item.findtext('staff', '1'))
            if staff not in (1, 2):
                raise ValueError('Unexpected staff in evaluation subset.')
            voice = item.findtext('voice', '1')
            chord = item.find('chord') is not None
            start = starts.get(voice, cursor) if chord else cursor
            if not chord:
                starts[voice] = start
            pitch_node = item.find('pitch')
            pitch = None
            if pitch_node is not None:
                pitch = (int(pitch_node.findtext('octave')) + 1) * 12
                pitch += dict(C=0,D=2,E=4,F=5,G=7,A=9,B=11)[pitch_node.findtext('step')]
                pitch += int(pitch_node.findtext('alter', '0'))
            ties = {tie.get('type') for tie in item.findall('tie') + item.findall('notations/tied')}
            events.append(event(measure, staff, start, duration, pitch, 'start' in ties, 'stop' in ties, voice))
            if not chord:
                cursor += duration
            extent = max(extent, start + duration, cursor)
        extents.append(dict(measure=measure, extent=extent))
    return events, dict(meters=meters, totalMeasures=len(measures), evaluatedMeasures=min(limit, len(measures)), measureExtents=extents)


def metric(reference, actual, fields):
    expected = Counter(tuple(e[field] for field in fields) for e in reference)
    observed = Counter(tuple(e[field] for field in fields) for e in actual)
    matched = sum((expected & observed).values())
    precision = matched / len(actual) if actual else 0
    recall = matched / len(reference) if reference else 0
    return dict(matched=matched, reference=len(reference), actual=len(actual),
                precision=round(precision, 6), recall=round(recall, 6),
                f1=round(2 * matched / (len(reference) + len(actual)), 6) if reference or actual else 0)


def compare(reference, actual):
    reference, actual = merge_ties(reference), merge_ties(actual)
    notes = [e for e in reference if e['pitch'] is not None]
    recognized = [e for e in actual if e['pitch'] is not None]
    result = {}
    for name, extra in [('pitchWithinMeasure', []), ('pitchAndDuration', ['duration']), ('pitchAndOnset', ['start']), ('exactNote', ['start', 'duration'])]:
        result[name] = metric(notes, recognized, ['measure', 'staff', 'pitch'] + extra)
    result['exactRest'] = metric([e for e in reference if e['pitch'] is None], [e for e in actual if e['pitch'] is None], ['measure', 'staff', 'start', 'duration'])
    sequence = []
    for staff in (1, 2):
        expected = [e['pitch'] for e in sorted(notes, key=lambda e:(e['measure'], e['start'], e['pitch'])) if e['staff'] == staff]
        observed = [e['pitch'] for e in sorted(recognized, key=lambda e:(e['measure'], e['start'], e['pitch'])) if e['staff'] == staff]
        sequence.append(dict(staff=staff, edits=edit_distance(expected, observed), reference=len(expected), actual=len(observed)))
    total_edits = sum(row['edits'] for row in sequence)
    result['pitchSequence'] = dict(edits=total_edits, reference=len(notes), actual=len(recognized),
                                  errorRate=round(total_edits / len(notes), 6), byStaff=sequence,
                                  scope='Pitch sequence only: ignores measures, onsets and durations; unit-cost substitutions/insertions/deletions.')
    fields = ['measure', 'staff', 'pitch', 'start', 'duration']
    expected = Counter(tuple(e[f] for f in fields) for e in notes)
    observed = Counter(tuple(e[f] for f in fields) for e in recognized)
    result['missingOrDifferent'] = [dict(zip(fields, row), count=count) for row, count in (expected-observed).items()]
    result['extraOrDifferent'] = [dict(zip(fields, row), count=count) for row, count in (observed-expected).items()]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('trial_directory', type=Path)
    parser.add_argument('--reference', type=Path, default=Path(__file__).parent/'reference/rice-field-scan.json')
    parser.add_argument('--allow-incomplete', action='store_true', help='Allow a progress snapshot before both Oemer runs finish.')
    parser.add_argument('--output', type=Path, help='Override the comparison JSON path.')
    args = parser.parse_args()
    root = args.trial_directory
    reference = read_reference(args.reference)
    reference_data = json.loads(args.reference.read_text(encoding='utf-8-sig'))
    limit = len(reference_data['upper'])
    if len(reference_data['lower']) != limit:
        raise ValueError('Reference staves have different measure counts.')
    systems = reference_data.get('systems', [3, 3, 4, 4, 3])
    count = 0
    covered = 0
    for size in systems:
        count += 1
        covered += size
        if covered >= limit:
            break
    if covered != limit:
        raise ValueError('Reference must end at a complete system boundary.')
    results = {}
    for quality in ('clear', 'blur'):
        smt = [root/f'results/scan-{quality}-space8-white-system-{i:02d}/recognized.tokens.txt' for i in range(1, count + 1)]
        events, details = read_smt(smt)
        results[f'smt-{quality}'] = dict(**compare(reference, events), details=details)
        xml = root/f'results/oemer-scan-{quality}/recognized.musicxml'
        if xml.exists():
            events, details = read_musicxml(xml, limit)
            results[f'oemer-{quality}'] = dict(**compare(reference, events), details=details)
        elif not args.allow_incomplete:
            raise FileNotFoundError(f'Finish the Oemer run first: {xml}')
    output = dict(scope=f'{limit} physical measures; measure-local timing; ties merged; meter and rests evaluated separately. This single score is not a general accuracy benchmark.',
                  referenceFile=str(args.reference.resolve()), referenceSha256=hashlib.sha256(args.reference.read_bytes()).hexdigest(), results=results)
    path = args.output or root/'results/oemer-smt-comparison.json'
    path.write_text(json.dumps(output,ensure_ascii=False,indent=2,default=str)+'\n',encoding='utf-8')
    print(json.dumps(output,ensure_ascii=False,indent=2,default=str))


if __name__ == '__main__':
    main()
