"""Run with the development Python containing numpy and Pillow."""

from pathlib import Path
import tempfile
import unittest

import numpy as np

from smt_scan_layout import barlines, detect_layout
from smt_scan_quality import assess, changes_unflagged_measures, choose


def scan(short_last=False, missing=False, orphan=False):
    image = np.full((520, 500), 255, dtype=np.uint8)
    for index, top in enumerate((50,150,300,400)):
        end = 240 if short_last and index >= 2 else 450
        for line in range(5):
            if not (missing and index == 0 and line == 2):
                image[top+line*10, 30:end] = 0
    for first, last, end in ((50,190,450),(300,440,240 if short_last else 450)):
        if not orphan or first == 50:
            for x in (30,(30+end)//2,end-1):
                image[first:last+1,x] = 0
    if orphan:
        image[400:441,:] = 255
    image[250,5:490] = 0  # unrelated horizontal rule
    return image


class LayoutTests(unittest.TestCase):
    def test_stray_rule_does_not_prevent_pairing(self):
        layout = detect_layout(scan())
        self.assertEqual([s['lines'][0] for s in layout['staves']], [50,150,300,400])
        self.assertGreaterEqual(layout['ignoredBands'],1)

    def test_short_last_system_is_not_omitted(self):
        layout = detect_layout(scan(short_last=True))
        self.assertEqual(len(layout['staves']),4)
        self.assertLess(layout['coverage'],0.5)

    def test_one_missing_horizontal_line(self):
        layout=detect_layout(scan(missing=True))
        self.assertEqual(layout['inferredLines'],1)
        self.assertEqual(layout['staves'][0]['lines'],[50,60,70,80,90])

    def test_blank_and_unpaired_staff_are_rejected(self):
        for image in (np.full((520,500),255,dtype=np.uint8),scan(orphan=True),scan(short_last=True,orphan=True)):
            with self.assertRaises(ValueError):
                detect_layout(image)

    def test_double_final_bar_is_one_boundary(self):
        image=scan()
        image[50:191,444]=0
        self.assertEqual(len(barlines(image,150,[50,60,70,80,90],[150,160,170,180,190],20,460)),3)

    def test_locally_visible_skewed_system_cannot_be_silently_omitted(self):
        image=scan()
        image[290:460,:]=255
        for x in range(30,450):
            shift=round(x*0.02)
            for top in (300,400):
                for line in range(5):
                    image[top+line*10+shift,x]=0
        for x in (30,240,449):
            shift=round(x*0.02)
            image[300+shift:441+shift,x]=0
        with self.assertRaisesRegex(ValueError,'outside the extracted layout'):
            detect_layout(image)


class QualityTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write(self,name,measures,meter='4/4'):
        rows=['**kern\t**kern']
        if meter:
            rows.append(f'*M{meter}\t*M{meter}')
        rows.append('=-\t=-')
        for text in measures:
            rows.extend(text.splitlines())
            rows.append('=\t=')
        rows.append('*-\t*-')
        path=self.root/name
        path.write_text('\n'.join(rows),encoding='utf-8')
        return path

    def test_consistent_three_four_is_not_forced_to_four_four(self):
        path=self.write('three.krn',['2.C\t2.c']*3,'3/4')
        quality=assess(path,3)
        self.assertEqual(quality['score'],0)
        self.assertEqual(quality['inferredQuarterBeatBudget'],'3')

    def test_header_conflict_and_underfull_staff_are_detected(self):
        path=self.write('bad.krn',['2.C\t1c','1C\t1c','1C\t1c'],'3/4')
        quality=assess(path,3)
        self.assertEqual(set(quality['issues']),{'meter_conflict','staff_duration'})
        self.assertEqual(quality['suspectMeasures'],[1])

    def test_candidate_cannot_evade_frozen_budget(self):
        baseline=self.write('baseline.krn',['1C\t1c']*3,None)
        candidate=self.write('candidate.krn',['2.C\t1c','1C\t2.c','2.C\t2.c'],None)
        budget=assess(baseline,3)['inferredQuarterBeatBudget']
        quality=assess(candidate,3,budget)
        self.assertGreater(quality['score'],0)
        self.assertGreater(quality['durationErrors'],0)

    def test_unknown_rhythm_is_not_success(self):
        path=self.write('unknown.krn',['2C\t4c'],None)
        self.assertIn('rhythm_unverified',assess(path,1)['issues'])

    def test_pickup_is_preserved_but_candidate_cannot_invent_one(self):
        path=self.write('pickup.krn',['4C\t4c','1C\t1c','1C\t1c'])
        quality=assess(path,3)
        self.assertEqual(quality['score'],0)
        self.assertEqual(quality['allowedShortMeasures'],[1])
        short=self.write('new-short.krn',['1C\t1c','1C\t1c','2.C\t2.c'])
        self.assertGreater(assess(short,3,'4',[])['score'],0)

    def test_real_meter_change_is_not_a_duration_error(self):
        path=self.write('change.krn',['1C\t1c','*M3/4\t*M3/4\n2.C\t2.c','2.C\t2.c'])
        self.assertEqual(assess(path,3)['score'],0)

    def test_null_rows_are_cosmetic_but_non_null_content_is_preserved(self):
        path=self.write('null.krn',['1C\t1c\n.\t.','1C\t1c','1C\t1c'])
        quality=assess(path,3)
        self.assertEqual(quality['score'],0)
        self.assertEqual(quality['ignoredNullRows'],1)

    def test_changed_unflagged_measure_is_rejected(self):
        baseline=self.write('original.krn',['1C\t1c','2.C\t1c','1C\t1c'])
        candidate=self.write('new.krn',['1D\t1c','1C\t1c','1C\t1c'])
        self.assertTrue(changes_unflagged_measures(baseline,candidate,assess(baseline,3)))
        repair=self.write('repair.krn',['1C\t1c']*3)
        self.assertFalse(changes_unflagged_measures(baseline,repair,assess(baseline,3)))

    def test_duplicate_voice_is_flagged_without_deleting_notes(self):
        path=self.root/'split.krn'
        path.write_text('**kern\t**kern\n*\t*^\n=-\t=-\t=-\n1C\t1c\t1c\n=\t=\t=\n*\t*v\t*v\n*-\t*-\n')
        quality=assess(path,1)
        self.assertEqual(quality['suspectedDuplicates'],1)
        self.assertEqual(quality['pitchedSymbols'],3)

    def test_selection_retains_baseline_on_ties_and_skips_rejected(self):
        baseline=dict(attempt=0,quality=dict(score=10))
        equal=dict(attempt=1,quality=dict(score=10))
        rejected=dict(attempt=2,quality=dict(score=0),rejected=True)
        self.assertIs(choose([baseline,equal,rejected]),baseline)

    def test_missing_termination_is_not_accepted(self):
        path=self.root/'truncated.krn';path.write_text('**kern\t**kern\n1C\t1c\n')
        self.assertIn('fatal',assess(path,1))

    def test_missing_bar_boundary_marks_measure_indices_uncertain(self):
        path=self.write('count.krn',['1C\t1c']*3)
        quality=assess(path,4)
        self.assertIn('measure_count',quality['issues'])
        self.assertEqual(quality['suspectMeasures'],[1,2,3,4])


if __name__=='__main__':
    unittest.main()
