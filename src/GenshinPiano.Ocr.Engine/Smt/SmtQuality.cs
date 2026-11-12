namespace GenshinPiano.Ocr.Engine.Smt;

internal sealed record SmtAssessment(double Score,SmtDocument? Document,Beat? Budget,HashSet<int> SuspectMeasures,HashSet<int> ShortMeasures,List<string> Issues,string? Fatal=null)
{
    public bool NeedsReview => Issues.Count>0;
}

internal static class SmtQuality
{
    public static SmtAssessment Assess(string tokens,int? expectedMeasures,SmtAssessment? baseline=null)
    {
        SmtDocument document;
        try { document=SmtNotation.Parse(tokens); }
        catch (Exception error) when (error is InvalidDataException or FormatException or OverflowException or DivideByZeroException)
        { return new(10000,null,null,[],[],["invalid_notation"],error.Message); }
        var lengths=document.Measures.SelectMany(m=>new[]{m.Lower,m.Upper}).Where(v=>v>Beat.Zero).ToArray();
        var modal=lengths.GroupBy(v=>v).OrderByDescending(g=>g.Count()).First();
        Beat? budget=baseline?.Budget ?? (modal.Count()>=3 && modal.Count()>lengths.Length/2d ? modal.Key : null);
        var changes=document.Meters.GroupBy(m=>m.Measure).ToDictionary(g=>g.Key,g=>g.Last());
        var suspect=new HashSet<int>();
        var shorts=new HashSet<int>();
        var conflicts=new HashSet<int>();
        SmtMeter? declared=null;
        var durationErrors=0;
        var distance=Beat.Zero;
        var unverified=0;
        foreach (var measure in document.Measures)
        {
            if (changes.TryGetValue(measure.Number,out var signature)) declared=signature;
            Beat? target=declared is not null && measure.Lower==declared.Length && measure.Upper==declared.Length ? declared.Length : budget ?? declared?.Length;
            if (target is null) { unverified++;suspect.Add(measure.Number);continue; }
            if (declared is not null && declared.Length!=target.Value) { conflicts.Add(declared.Measure);suspect.Add(declared.Measure); }
            if ((measure.Number==1 || measure.Number==document.Measures.Count) && measure.Lower==measure.Upper
                && measure.Lower>Beat.Zero && measure.Lower<target.Value && (baseline is null || baseline.ShortMeasures.Contains(measure.Number)))
            { shorts.Add(measure.Number);continue; }
            foreach (var value in new[]{measure.Lower,measure.Upper})
                if (value!=target.Value) { durationErrors++;distance+=(value-target.Value).Abs();suspect.Add(measure.Number); }
        }
        var duplicateGroups=document.Events.Where(e=>e.Pitch is not null).GroupBy(e=>(e.Measure,e.Staff,e.Start,e.Duration,e.Pitch)).Where(g=>g.Count()>1).ToArray();
        var duplicates=duplicateGroups.Sum(g=>g.Count()-1);
        foreach (var group in duplicateGroups) suspect.Add(group.Key.Measure);
        var active=new Dictionary<(int Staff,int Voice,int Pitch),SmtEvent>();
        var tieErrors=0;
        foreach (var item in document.Events.Where(e=>e.Pitch is not null).OrderBy(e=>e.Measure).ThenBy(e=>e.Start))
        {
            var key=(item.Staff,item.Voice,item.Pitch!.Value);
            if (item.TieStop)
            {
                if (!active.Remove(key) && !(item.Measure==1 && item.Start==Beat.Zero)) { tieErrors++;suspect.Add(item.Measure); }
            }
            if (item.TieStart)
            {
                if (active.ContainsKey(key)) { tieErrors++;suspect.Add(item.Measure); }
                active[key]=item;
            }
        }
        // Open ties exactly at the page boundary can continue on another page.
        foreach (var item in active.Values)
            if (item.Measure!=document.Measures.Count || item.Start+item.Duration!=document.Measures[^1].Extent)
            { tieErrors++;suspect.Add(item.Measure); }
        var countError=expectedMeasures is { } expected ? Math.Abs(document.Measures.Count-expected) : 0;
        if (countError>0) suspect.UnionWith(Enumerable.Range(1,Math.Max(document.Measures.Count,expectedMeasures!.Value)));
        var issues=new List<string>();
        if (countError>0) issues.Add("measure_count");
        if (durationErrors>0) issues.Add("staff_duration");
        if (conflicts.Count>0) issues.Add("meter_conflict");
        if (tieErrors>0) issues.Add("tie");
        if (duplicates>0) issues.Add("suspected_duplicate");
        if (unverified>0) issues.Add("rhythm_unverified");
        var score=countError*1000+durationErrors*20+distance.Value*10+conflicts.Count*15+tieErrors*10+duplicates*10+unverified*25;
        return new(score,document,budget,suspect,shorts,issues);
    }

    public static bool ChangesUnflaggedMeasures(SmtAssessment baseline,SmtAssessment candidate)
    {
        if (baseline.Document is null || candidate.Document is null) return false;
        static Dictionary<(int,int,Beat,Beat,int?),int> Signatures(SmtDocument doc,HashSet<int> flagged) => doc.Events
            .Where(e=>!flagged.Contains(e.Measure)).GroupBy(e=>(e.Measure,e.Staff,e.Start,e.Duration,e.Pitch)).ToDictionary(g=>g.Key,g=>g.Count());
        var before=Signatures(baseline.Document,baseline.SuspectMeasures);
        var after=Signatures(candidate.Document,baseline.SuspectMeasures);
        return before.Count!=after.Count || before.Any(pair=>after.GetValueOrDefault(pair.Key)!=pair.Value);
    }
}
