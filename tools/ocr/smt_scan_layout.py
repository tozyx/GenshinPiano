"""Geometry-only layout detection for straight, single-piano scans."""

import numpy as np


SCAN_INPUT_HINT = 'Use a clear, flat scan or directly rendered score image; automatic photo rectification is not supported.'


def bands(indices):
    result = []
    for value in indices:
        if not result or value > result[-1][-1] + 1:
            result.append([int(value)])
        else:
            result[-1].append(int(value))
    return result


def _staves(gray, threshold, coverage):
    projection = (gray < threshold).sum(axis=1)
    centers = [round(sum(group) / len(group)) for group in bands(np.flatnonzero(projection > gray.shape[1] * coverage))]
    candidates = []
    for i in range(len(centers) - 4):
        lines = centers[i:i + 5]
        gaps = np.diff(lines)
        spacing = float(np.median(gaps))
        if spacing < 3 or spacing > gray.shape[0] / 10:
            continue
        if max(abs(gaps - spacing)) > max(1, spacing * 0.18):
            continue
        candidates.append(dict(lines=lines, spacing=spacing, inferred=[], strength=float(np.mean(projection[lines]))))
    selected = []
    for candidate in sorted(candidates, key=lambda row: -row['strength']):
        if not any(candidate['lines'][0] <= other['lines'][-1] + other['spacing'] / 2 and
                   candidate['lines'][-1] >= other['lines'][0] - other['spacing'] / 2 for other in selected):
            selected.append(candidate)
    if not selected:
        return [], centers
    spacing = float(np.median([staff['spacing'] for staff in selected]))
    for center in centers:
        for position in range(5):
            top = center - spacing * position
            if top < 0 or top + spacing * 4 >= gray.shape[0]:
                continue
            lines, inferred = [], []
            for offset in range(5):
                expected = top + offset * spacing
                nearest = min(centers, key=lambda row: abs(row - expected))
                if abs(nearest - expected) <= max(1, spacing * 0.16):
                    lines.append(nearest)
                else:
                    lines.append(round(expected))
                    inferred.append(offset)
            if len(inferred) != 1 or len(set(lines)) != 5:
                continue
            if not any(lines[0] <= other['lines'][-1] + spacing / 2 and lines[-1] >= other['lines'][0] - spacing / 2 for other in selected):
                selected.append(dict(lines=lines, spacing=spacing, inferred=inferred, strength=0))
    return sorted(selected, key=lambda row: row['lines'][0]), centers


def _connections(gray, threshold, upper, lower):
    top, bottom = upper['lines'][-1] + 1, lower['lines'][0]
    if bottom <= top:
        return []
    projection = (gray[top:bottom] < threshold).sum(axis=0)
    return [round(sum(group) / len(group)) for group in bands(np.flatnonzero(projection > (bottom - top) * 0.75))]


def barlines(gray, threshold, upper, lower, left, right):
    top, bottom = upper[0], lower[-1] + 1
    projection = (gray[top:bottom, left:right] < threshold).sum(axis=0)
    positions = [left + round(sum(group) / len(group)) for group in bands(np.flatnonzero(projection > (bottom - top) * 0.7))]
    spacing = float(np.median(np.concatenate((np.diff(upper), np.diff(lower)))))
    merged = []
    for x in positions:
        if merged and x - merged[-1] < spacing * 0.8:
            merged[-1] = x
        else:
            merged.append(x)
    return merged


def audit_local_coverage(gray, global_staves):
    """Find staff-like tracks outside the accepted layout in multiple slices.

    This rejects incomplete extraction; it does not straighten photographs or
    infer how many systems a particular song should contain.
    """
    spacing = float(np.median([staff['spacing'] for staff in global_staves]))
    observations = []
    for slice_id, fraction in enumerate((0.2, 0.4, 0.6, 0.8)):
        center = round(gray.shape[1] * fraction)
        half = max(4, round(gray.shape[1] * 0.06))
        part = gray[:, max(0,center-half):min(gray.shape[1],center+half)]
        local = []
        for threshold in (150,180,200,220):
            candidates, _ = _staves(part,threshold,0.55)
            if len(candidates) > len(local):
                local = candidates
        for staff in local:
            if staff['inferred'] or not 0.75*spacing <= staff['spacing'] <= 1.3*spacing:
                continue
            middle = float(np.mean(staff['lines']))
            if any(abs(middle-np.mean(candidate['lines'])) <= max(2,1.5*spacing) for candidate in global_staves):
                continue
            observations.append(dict(middle=middle,slice=slice_id))
    clusters = []
    for observation in sorted(observations,key=lambda row:row['middle']):
        matching = next((cluster for cluster in clusters
                         if abs(observation['middle']-np.mean([item['middle'] for item in cluster])) <= 2.5*spacing),None)
        if matching is None:
            clusters.append([observation])
        else:
            matching.append(observation)
    return [dict(centerY=round(float(np.mean([item['middle'] for item in cluster])),2),
                 supportingSlices=len({item['slice'] for item in cluster}))
            for cluster in clusters if len({item['slice'] for item in cluster}) >= 2]


def detect_layout(gray):
    if gray.ndim != 2 or min(gray.shape) < 40:
        raise ValueError('Expected a grayscale scan of at least 40 pixels per side.')
    choices = []
    observed = {}
    for threshold in (150, 180, 200, 220):
        for coverage in (0.5, 0.35, 0.22):
            staves, centers = _staves(gray, threshold, coverage)
            observed[threshold] = max(observed.get(threshold, 0), len(staves))
            if not staves or len(staves) % 2:
                continue
            pairs = []
            for i in range(0, len(staves), 2):
                upper, lower = staves[i:i + 2]
                if abs(upper['spacing'] - lower['spacing']) > max(1, upper['spacing'] * 0.2):
                    break
                if len(_connections(gray, threshold, upper, lower)) < 2:
                    break
                pairs.append((upper, lower))
            if len(pairs) * 2 != len(staves):
                continue
            choices.append(dict(staves=staves, threshold=threshold, coverage=coverage,
                                inferredLines=sum(len(s['inferred']) for s in staves),
                                ignoredBands=len(centers) - sum(5-len(s['inferred']) for s in staves)))
    if not choices:
        raise ValueError('Cannot reliably pair piano staves with connecting barlines. '+SCAN_INPUT_HINT)
    best = min(choices, key=lambda c:(-len(c['staves']), c['inferredLines'], -c['coverage'], c['threshold']))
    if observed[best['threshold']] > len(best['staves']):
        raise ValueError('Unpaired plausible staff detected; refusing to silently omit part of the page. '+SCAN_INPUT_HINT)
    uncovered = audit_local_coverage(gray,best['staves'])
    if uncovered:
        raise ValueError(f'Local staff tracks are outside the extracted layout ({len(uncovered)} supported tracks); '
                         'page may be curved/skewed or partially omitted. '+SCAN_INPUT_HINT)
    best['coverageAudit'] = 'passed-four-local-slices'
    return best
