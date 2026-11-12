using GenshinPiano.Core.Scores;

namespace GenshinPiano.Ocr.Engine.Smt;

internal static class SmtScoreBuilder
{
    public static ScoreDocument Build(IReadOnlyList<SmtDocument> systems, string title, bool accompaniment, List<string> warnings)
    {
        var tracks = new Dictionary<int,List<NoteEvent>> { [1]=[], [2]=[] };
        var open = new Dictionary<(int Staff,int Voice,int Pitch), (List<NoteEvent> Notes,int Index,Beat End)>();
        var signatures = new List<TimeSignatureChange>();
        var start = Beat.Zero;
        foreach (var system in systems)
        {
            var offsets = new Dictionary<int,Beat>();
            foreach (var measure in system.Measures) { offsets[measure.Number]=start; start+=measure.Extent; }
            foreach (var meter in system.Meters)
            {
                var signature = new TimeSignatureChange { Tick=offsets.GetValueOrDefault(meter.Measure,start).Ticks(), Numerator=meter.Numerator, Denominator=meter.Denominator };
                if (signatures.Count == 0 || signatures[^1].Numerator != signature.Numerator || signatures[^1].Denominator != signature.Denominator)
                    signatures.Add(signature);
            }
            foreach (var item in system.Events.Where(e => e.Pitch is not null).OrderBy(e => e.Measure).ThenBy(e => e.Start))
            {
                if (!accompaniment && item.Staff == 2) continue;
                var tick = offsets[item.Measure]+item.Start;
                var key = (item.Staff,item.Voice,item.Pitch!.Value);
                if (item.TieStop && open.TryGetValue(key,out var prior))
                {
                    if (prior.End != tick) throw new InvalidDataException("Non-contiguous SMT tie requires manual review.");
                    var old = prior.Notes[prior.Index];
                    var length = (tick+item.Duration).Ticks()-old.StartTick;
                    prior.Notes[prior.Index]=old with { DurationTick=length, RhythmTick=length };
                    if (item.TieStart) open[key]=(prior.Notes,prior.Index,tick+item.Duration); else open.Remove(key);
                    continue;
                }
                if (item.TieStop)
                {
                    if (tick>Beat.Zero) throw new InvalidDataException("An SMT tie continuation has no matching start.");
                    warnings.Add("An incoming tie could not be matched; review tied notes.");
                }
                var notes = tracks[item.Staff];
                var duration = Math.Max(1,(tick+item.Duration).Ticks()-tick.Ticks());
                notes.Add(new NoteEvent { Pitch=item.Pitch.Value, StartTick=tick.Ticks(), DurationTick=duration, RhythmTick=duration });
                if (item.TieStart) open[key]=(notes,notes.Count-1,tick+item.Duration);
            }
        }
        if (open.Count > 0) warnings.Add("The image ends with open ties; review the ending durations.");
        if (signatures.Count == 0 || signatures[0].Tick > 0)
        {
            signatures.Insert(0,new TimeSignatureChange());
            warnings.Add("No initial time signature was recognized; 4/4 is used for display. Review the time signature.");
        }
        var tempo = systems.Select(s => s.Tempo).FirstOrDefault(t => t is not null);
        if (tempo is null) warnings.Add("Tempo was not recognized; the initial tempo is 120 BPM. Adjust it after importing.");
        return new ScoreDocument
        {
            Metadata=new ScoreMetadata { Title=title },
            Timing=new TimingDefinition { TempoMap=[new TempoChange { Bpm=tempo ?? 120 }],TimeSignatures=signatures },
            Playback=new PlaybackSettings { Mapping="full-pitch",OutOfRangePolicy=OutOfRangePolicy.Drop },
            Tracks=tracks.Where(pair => (accompaniment || pair.Key == 1) && pair.Value.Count > 0).OrderBy(p => p.Key)
                .Select(pair => new ScoreTrack { Id=$"smt-staff-{pair.Key}",Name=$"Piano - Staff {pair.Key}",Instrument="piano",Notes=pair.Value.OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).ToList() }).ToList(),
        };
    }
}
