"""Offline scoring after reference-free scan candidate selection has finished."""

import argparse
import hashlib
import json
from pathlib import Path

from compare_staff_ocr import compare, read_reference
from smt_notation import read_smt


def evaluate(reference_path, report_paths):
    reference = read_reference(reference_path)
    cases = []
    for path in report_paths:
        report = json.loads(path.read_text(encoding='utf-8-sig'))
        baseline = [Path(row['candidates'][0]['tokens']) for row in report['systems']]
        selected = [Path(row['selectedTokens']) for row in report['systems']]
        before, before_details = read_smt(baseline)
        after, after_details = read_smt(selected)
        cases.append(dict(report=str(path.resolve()), source=report['source'],
                          before=compare(reference,before),after=compare(reference,after),
                          beforeDetails=before_details,afterDetails=after_details,
                          elapsedSeconds=report['elapsedSeconds'],
                          baselineInferenceSeconds=report['baselineInferenceSeconds'],
                          totalInferenceSeconds=report['totalInferenceSeconds'],
                          retriedSystems=report['retriedSystems'],unresolvedSystems=report['unresolvedSystems'],
                          selections=[dict(system=row['system'],attempt=row['selectedAttempt'],
                                           spacing=row['candidates'][row['selectedAttempt']]['spacing'],
                                           suppressFaint=row['candidates'][row['selectedAttempt']]['suppressFaint'],
                                           unresolved=row['unresolved']) for row in report['systems']]))
    return dict(scope='Final offline scoring; references are never supplied to recognition or candidate selection.',
                reference=str(reference_path.resolve()),referenceSha256=hashlib.sha256(reference_path.read_bytes()).hexdigest(),cases=cases)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reports',type=Path,nargs='+')
    parser.add_argument('--reference',type=Path,default=Path(__file__).parent/'reference/rice-field-scan.json')
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    result=evaluate(args.reference,args.reports)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2,default=str)+'\n',encoding='utf-8')
    for case in result['cases']:
        print(json.dumps(dict(source=case['source'],before=case['before']['exactNote'],after=case['after']['exactNote'],
                              beforePitchEdits=case['before']['pitchSequence']['edits'],afterPitchEdits=case['after']['pitchSequence']['edits'],
                              elapsedSeconds=case['elapsedSeconds'],unresolvedSystems=case['unresolvedSystems']),ensure_ascii=False))


if __name__=='__main__':
    main()
