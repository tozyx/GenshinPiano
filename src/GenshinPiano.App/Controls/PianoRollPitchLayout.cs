using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public enum PianoRollPitchLayoutMode
{
    Genshin21,
    Piano88,
    UsedRange,
}

public readonly record struct PianoRollPitchRow(
    int Pitch,
    GenshinKey? Key,
    bool IsBlackKey);

public static class PianoRollPitchLayouts
{
    private static readonly PianoRollPitchRow[] GenshinRows = GenshinKeyMap.All
        .Reverse()
        .Select(entry => new PianoRollPitchRow(entry.Pitch, entry.Key, false))
        .ToArray();

    private static readonly PianoRollPitchRow[] PianoRows = Enumerable
        .Range(21, 88)
        .Reverse()
        .Select(pitch => new PianoRollPitchRow(
            pitch,
            GenshinKeyMap.TryMapPitch(pitch, 0, OutOfRangePolicy.Reject, out var key) ? key : null,
            pitch % 12 is 1 or 3 or 6 or 8 or 10))
        .ToArray();

    public static IReadOnlyList<PianoRollPitchRow> GetRows(
        PianoRollPitchLayoutMode mode,
        IEnumerable<int>? scorePitches = null)
    {
        if (mode == PianoRollPitchLayoutMode.Genshin21)
        {
            return GenshinRows;
        }
        if (mode == PianoRollPitchLayoutMode.Piano88)
        {
            return PianoRows;
        }

        var pitches = scorePitches?.Where(pitch => pitch is >= 0 and <= 127).ToArray() ?? [];
        if (pitches.Length == 0)
        {
            return GenshinRows;
        }
        var minimum = Math.Clamp(pitches.Min() - 2, 0, 127);
        var maximum = Math.Clamp(pitches.Max() + 2, 0, 127);
        if (maximum - minimum < 11)
        {
            var missing = 11 - (maximum - minimum);
            minimum = Math.Max(0, minimum - missing / 2);
            maximum = Math.Min(127, minimum + 11);
            minimum = Math.Max(0, maximum - 11);
        }
        return Enumerable.Range(minimum, maximum - minimum + 1).Reverse()
            .Select(pitch => new PianoRollPitchRow(
                pitch,
                GenshinKeyMap.TryMapPitch(pitch, 0, OutOfRangePolicy.Reject, out var key) ? key : null,
                pitch % 12 is 1 or 3 or 6 or 8 or 10))
            .ToArray();
    }
}
