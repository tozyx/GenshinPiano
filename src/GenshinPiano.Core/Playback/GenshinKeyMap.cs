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

    public static IReadOnlyList<(GenshinKey Key, int Pitch)> All { get; } =
        Array.AsReadOnly(Entries);

    public static bool TryGetPitch(GenshinKey key, out int pitch) => KeyToPitch.TryGetValue(key, out pitch);

    public static bool TryMapPitch(
        int pitch,
        int transpose,
        OutOfRangePolicy policy,
        out GenshinKey key)
    {
        var mappedPitch = pitch + transpose;
        if (policy == OutOfRangePolicy.OctaveFold)
        {
            while (mappedPitch < 48)
            {
                mappedPitch += 12;
            }

            while (mappedPitch > 83)
            {
                mappedPitch -= 12;
            }
        }

        return PitchToKey.TryGetValue(mappedPitch, out key);
    }
}
