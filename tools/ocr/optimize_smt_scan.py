"""First-round SMT scan pipeline: layout, validation and bounded local retry.

Development tool, not the released app backend. Requires Pillow and numpy for
preprocessing. Inference itself uses the pinned native CPU runtime. No score
reference, expected pitches, song identity, or annotation is read here.
Input scope: clear, flat scans/rendered score images. Automatic rectification
and support for photographed pages are outside this backend's scope.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
import zipfile

from PIL import Image
import numpy as np

from prepare_smt_scan import prepare
from smt_scan_quality import assess, changes_unflagged_measures, choose


ARCHIVE_SHA256 = 'e82337bcdefb1d54b0d97c26fc7e64ff35847d1db285e5062d6674df91ce6017'
MODEL_SHA256 = 'f6612885a8d7015bef5ab7f045a37a0f667575f4b65b324a353cf348d6edc752'
VARIANTS = ((9, True), (8, False), (7, True))


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, default=str)+'\n', encoding='utf-8')


def render_variant(source, system, destination, spacing, suppress):
    crop = source.crop(system['crop']).convert('L')
    if suppress:
        pixels = np.asarray(crop).copy()
        pixels[pixels > 210] = 255
        crop = Image.fromarray(pixels)
    lines = system['staffLines']
    original_spacing = float(np.median(np.concatenate((np.diff(lines[:5]), np.diff(lines[5:])))))
    scale = spacing / original_spacing
    resized = crop.resize((max(1,round(crop.width*scale)), max(1,round(crop.height*scale))), Image.Resampling.BILINEAR)
    canvas = Image.new('L',(resized.width,max(256,resized.height)),255)
    canvas.paste(resized,(0,0))
    canvas.save(destination)


def infer(executable, model, image, directory, threads, timeout):
    directory.mkdir()
    started = time.perf_counter()
    command = [str(executable), '-m',str(model), '-t',str(threads), '--ocr',str(image)]
    try:
        result = subprocess.run(command, capture_output=True, timeout=timeout, cwd=executable.parent,
                                creationflags=subprocess.CREATE_NO_WINDOW,
                                env={**os.environ,'SMT_OCR_FORCE_CPU':'1'})
        stdout, stderr, code = result.stdout, result.stderr, result.returncode
        timed_out = False
    except subprocess.TimeoutExpired as error:
        stdout, stderr, code = error.stdout or b'', error.stderr or b'', None
        timed_out = True
    path = directory/'recognized.tokens.txt'
    path.write_bytes(stdout)
    (directory/'stderr.log').write_bytes(stderr)
    metrics = dict(elapsedSeconds=round(time.perf_counter()-started,3), exitCode=code, timedOut=timed_out,
                   imageSha256=hashlib.sha256(image.read_bytes()).hexdigest(), threads=threads)
    save_json(directory/'metrics.json',metrics)
    return path, metrics


def run(image, trial, output, threads=4, timeout=60):
    if output.exists():
        raise ValueError('Output directory already exists; preserve previous evidence.')
    archive, model = trial/'crispembed-windows-x86_64.zip', trial/'smt-grandstaff-q8_0.gguf'
    if not model.is_file():
        model = trial/'models'/model.name
    for path, digest in ((archive,ARCHIVE_SHA256),(model,MODEL_SHA256)):
        if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            raise ValueError(f'Artifact SHA-256 mismatch: {path}')
    started = time.perf_counter()
    output.mkdir(parents=True)
    native = output/'native'
    with zipfile.ZipFile(archive) as package:
        package.extractall(native)
    executable = native/'crispembed.exe'
    prepared = output/'prepared'
    try:
        layout = prepare(image,prepared,8,True)
    except ValueError as error:
        save_json(output/'report.json',dict(status='layout_rejected',source=str(image),
                  sourceSha256=hashlib.sha256(image.read_bytes()).hexdigest(),error=str(error),
                  elapsedSeconds=round(time.perf_counter()-started,3),systems=[],unresolvedSystems=None))
        raise
    source = Image.open(image).convert('RGB')
    records = []
    for number, system in enumerate(layout['systems'],1):
        row = output/f'system-{number:02d}'
        row.mkdir()
        candidates = []
        for attempt, (spacing,suppress) in enumerate(((8,True),)+VARIANTS):
            if attempt:
                if not choose(candidates)['quality']['retry']:
                    break
                input_path = row/f'candidate-{attempt}.png'
                render_variant(source,system,input_path,spacing,suppress)
            else:
                input_path = prepared/system['file']
            path, metrics = infer(executable,model,input_path,row/f'candidate-{attempt}',threads,timeout)
            frozen_budget = candidates[0]['quality'].get('inferredQuarterBeatBudget') if attempt else None
            allowed_shorts = candidates[0]['quality'].get('allowedShortMeasures',[]) if attempt else None
            quality = assess(path,system['expectedMeasures'],frozen_budget,allowed_shorts)
            if metrics['exitCode'] != 0 or metrics['timedOut']:
                quality = dict(score=10000,retry=True,fatal='backend_failure',issues=['backend_failure'])
            candidate = dict(attempt=attempt,spacing=spacing,suppressFaint=suppress, tokens=str(path.resolve()),
                             metrics=metrics,quality=quality)
            if attempt and 'fatal' not in quality and 'fatal' not in candidates[0]['quality']:
                candidate['rejected'] = changes_unflagged_measures(Path(candidates[0]['tokens']),path,candidates[0]['quality'])
                if candidate['rejected']:
                    candidate['rejectionReason'] = 'changes_unflagged_measure'
            candidates.append(candidate)
            save_json(row/f'candidate-{attempt}'/'assessment.json',quality)
            print(f"System {number}, attempt {attempt}: score={quality['score']}, issues={quality['issues']}, rejected={candidate.get('rejected',False)}",flush=True)
        selected = choose(candidates)
        record = dict(system=number,selectedAttempt=selected['attempt'],selectedTokens=selected['tokens'],
                      unresolved=selected['quality']['retry'],candidates=candidates)
        save_json(row/'selection.json',record)
        records.append(record)
    fatal = any('fatal' in choose(r['candidates'])['quality'] for r in records)
    status = 'recognition_failed' if fatal else ('needs_review' if any(r['unresolved'] for r in records) else 'no_rule_anomaly')
    report = dict(status=status,source=str(image),sourceSha256=hashlib.sha256(image.read_bytes()).hexdigest(),
                  archiveSha256=ARCHIVE_SHA256,modelSha256=MODEL_SHA256,threads=threads,
                  elapsedSeconds=round(time.perf_counter()-started,3),
                  baselineInferenceSeconds=round(sum(r['candidates'][0]['metrics']['elapsedSeconds'] for r in records),3),
                  totalInferenceSeconds=round(sum(c['metrics']['elapsedSeconds'] for r in records for c in r['candidates']),3),
                  retriedSystems=sum(len(r['candidates'])>1 for r in records),
                  unresolvedSystems=sum(r['unresolved'] for r in records),systems=records,
                  selectionPolicy='Geometry/structure/duration/tie/duplicate checks only; stable baseline preference on equal score.',
                  scope='Clear, flat piano scans/rendered images only; photographed pages and automatic rectification are unsupported. No automatic note deletion, meter rewriting, or reference-based selection.')
    save_json(output/'report.json',report)
    if fatal:
        raise RuntimeError('Some systems have no usable candidate; inspect report.json.')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image',type=Path)
    parser.add_argument('output',type=Path)
    parser.add_argument('--trial-directory',type=Path,default=Path(__file__).resolve().parents[2]/'publish/smt-experiment')
    parser.add_argument('--threads',type=int,default=4)
    parser.add_argument('--timeout',type=int,default=60)
    args=parser.parse_args()
    if not 1<=args.threads<=64 or not 1<=args.timeout<=3600:
        parser.error('Threads must be 1..64 and timeout 1..3600 seconds.')
    report=run(args.image.resolve(),args.trial_directory.resolve(),args.output.resolve(),args.threads,args.timeout)
    print(json.dumps({key:value for key,value in report.items() if key!='systems'},ensure_ascii=False,indent=2))


if __name__=='__main__':
    main()
