using GenshinPiano.Core.Scores;

namespace GenshinPiano.Core.Playback;

public sealed record MidiNoteValue(int Pitch, int Velocity);

public sealed record ScheduledMidiChange(
    long Tick,
    TimeSpan Offset,
    IReadOnlyList<int> NotesOff,
    IReadOnlyList<MidiNoteValue> NotesOn);

public sealed record ScoreAuditionPlan(
    IReadOnlyList<ScheduledMidiChange> Events,
    long DurationTick,
    TimeSpan Duration);

public static class ScoreAuditionPlanner
{
    public static ScoreAuditionPlan Create(ScoreDocument score, bool naturalSustain = false)
    {
        ArgumentNullException.ThrowIfNull(score);
        var changes = new Dictionary<long, ChangeBuilder>();
        foreach (var track in score.Tracks.Where(track => !track.IsMuted))
        {
            var starts = track.Notes.Select(note => note.StartTick).Distinct().Order().ToArray();
            var nextStarts = starts.Select((tick, index) => new
            {
                Tick = tick,
                Next = index + 1 < starts.Length ? (long?)starts[index + 1] : null,
            }).ToDictionary(item => item.Tick, item => item.Next);

            var nextSamePitchStarts = track.Notes
                .GroupBy(note => note.Pitch)
                .SelectMany(group =>
                {
                    var pitchStarts = group.Select(note => note.StartTick)
                        .Distinct()
                        .Order()
                        .ToArray();
                    var nextByStart = pitchStarts.Select((tick, index) => new
                    {
                        Tick = tick,
                        Next = index + 1 < pitchStarts.Length
                            ? (long?)pitchStarts[index + 1]
                            : null,
                    }).ToDictionary(item => item.Tick, item => item.Next);
                    return group.Select(note => new
                    {
                        note.Id,
                        Next = nextByStart[note.StartTick],
                    });
                })
                .ToDictionary(item => item.Id, item => item.Next);

            foreach (var note in track.Notes)
            {
                var duration = NoteDurationCalculator.ResolveDuration(
                    note,
                    nextStarts[note.StartTick],
                    score.Timing.Ppq);
                if (naturalSustain)
                {
                    var rhythmTick = nextStarts[note.StartTick] is { } nextStart
                        ? nextStart - note.StartTick
                        : score.Timing.Ppq;
                    var naturalDuration = Math.Max(
                        1,
                        (long)Math.Round(
                            rhythmTick * NoteDurationCalculator.GetGateRatio(NoteArticulation.Natural),
                            MidpointRounding.AwayFromZero));
                    duration = Math.Max(duration, naturalDuration);

                    if (nextSamePitchStarts[note.Id] is { } nextSamePitch)
                    {
                        duration = Math.Min(duration, Math.Max(1, nextSamePitch - note.StartTick));
                    }
                }
                GetChange(changes, note.StartTick).NotesOn.Add(new MidiNoteValue(note.Pitch, note.Velocity));
                GetChange(changes, checked(note.StartTick + duration)).NotesOff.Add(note.Pitch);
            }
        }

        var events = changes.OrderBy(pair => pair.Key)
            .Select(pair => new ScheduledMidiChange(
                pair.Key,
                ScorePlaybackPlanner.TickToTime(pair.Key, score.Timing),
                pair.Value.NotesOff,
                pair.Value.NotesOn))
            .ToArray();
        var durationTick = events.LastOrDefault()?.Tick ?? 0;
        return new ScoreAuditionPlan(
            events,
            durationTick,
            ScorePlaybackPlanner.TickToTime(durationTick, score.Timing));
    }

    private static ChangeBuilder GetChange(IDictionary<long, ChangeBuilder> changes, long tick)
    {
        if (!changes.TryGetValue(tick, out var change))
        {
            change = new ChangeBuilder();
            changes.Add(tick, change);
        }

        return change;
    }

    private sealed class ChangeBuilder
    {
        public List<int> NotesOff { get; } = [];
        public List<MidiNoteValue> NotesOn { get; } = [];
    }
}
