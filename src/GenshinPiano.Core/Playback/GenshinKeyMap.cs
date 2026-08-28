using GenshinPiano.Core.Scores;

namespace GenshinPiano.Core.Playback;

public static class GenshinKeyMap
{
    private static readonly (GenshinKey Key, int Pitch)[] Entries =
    [
        (GenshinKey.Z, 48), (GenshinKey.X, 50), (GenshinKey.C, 52), (GenshinKey.V, 53),
        (GenshinKey.B, 55), (GenshinKey.N, 57), (GenshinKey.M, 59),
        (GenshinKey.A, 60), (GenshinKey.S, 62), (GenshinKey.D, 64), (GenshinKey.F, 65),
        (GenshinKey.G, 67), (GenshinKey.H, 69), (GenshinKey.J, 71),
        (GenshinKey.Q, 72), (GenshinKey.W, 74), (GenshinKey.E, 76), (GenshinKey.R, 77),
        (GenshinKey.T, 79), (GenshinKey.Y, 81), (GenshinKey.U, 83),
    ];

    private static readonly IReadOnlyDictionary<int, GenshinKey> PitchToKey =
        Entries.ToDictionary(entry => entry.Pitch, entry => entry.Key);

    private static readonly IReadOnlyDictionary<GenshinKey, int> KeyToPitch =
        Entries.ToDictionary(entry => entry.Key, entry => entry.Pitch);

    private static readonly IReadOnlyDictionary<int, int> PitchToIndex =
        Entries.Select((entry, index) => (entry.Pitch, Index: index))
            .ToDictionary(entry => entry.Pitch, entry => entry.Index);

    public static IReadOnlyList<(GenshinKey Key, int Pitch)> All { get; } =
        Array.AsReadOnly(Entries);

    public static bool TryGetPitch(GenshinKey key, out int pitch) => KeyToPitch.TryGetValue(key, out pitch);

    public static bool TryGetKeyIndex(int pitch, out int index) => PitchToIndex.TryGetValue(pitch, out index);

    public static bool TryShiftPitch(int pitch, int keySteps, out int shiftedPitch)
    {
        if (PitchToIndex.TryGetValue(pitch, out var index) &&
            index + keySteps is >= 0 and < 21)
        {
            shiftedPitch = Entries[index + keySteps].Pitch;
            return true;
        }

        shiftedPitch = default;
        return false;
    }

    public static bool TryMapPitch(
        int pitch,
        int transpose,
        OutOfRangePolicy policy,
        out GenshinKey key)
    {
        var mappedPitch = (long)pitch + transpose;
        if (policy == OutOfRangePolicy.OctaveFold)
        {
            if (mappedPitch < 48)
            {
                mappedPitch += ((48 - mappedPitch + 11) / 12) * 12;
            }
            else if (mappedPitch > 83)
            {
                mappedPitch -= ((mappedPitch - 83 + 11) / 12) * 12;
            }
        }

        if (mappedPitch is >= int.MinValue and <= int.MaxValue)
        {
            return PitchToKey.TryGetValue((int)mappedPitch, out key);
        }

        key = default;
        return false;
    }
}
