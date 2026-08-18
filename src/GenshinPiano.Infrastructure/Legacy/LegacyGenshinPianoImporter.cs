using System.Text;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.Legacy;

public sealed class LegacyGenshinPianoImporter : ILegacyScoreImporter
{
    private const double LegacyUnitsPerBeat = 4;

    private static readonly IReadOnlyDictionary<char, double> DurationUnits =
        new Dictionary<char, double>
        {
            ['b'] = 0.5,
            ['1'] = 1,
            ['2'] = 2,
            ['4'] = 4,
            ['8'] = 8,
            ['s'] = 12,
            ['c'] = 0.75,
            ['y'] = 1.5,
            ['3'] = 3,
            ['5'] = 6,
            ['9'] = 12,
            ['d'] = 18,
        };

    public async Task<ScoreDocument> LoadAsync(
        string path,
        LegacyImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LegacyImportOptions();
        ValidateOptions(options);

        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);
        var notes = new List<NoteEvent>();
        long currentTick = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[lineIndex].Trim();
            if (line.Length == 0 || line.StartsWith('|'))
            {
                continue;
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !tokens[0].All(character => character is >= 'A' and <= 'Z'))
            {
                throw CreateFormatException(path, lineIndex, "Expected a key chord followed by a duration code.");
            }

            var durationCode = tokens[1][0];
            if (!DurationUnits.TryGetValue(durationCode, out var durationUnits))
            {
                throw CreateFormatException(path, lineIndex, $"Unknown duration code '{durationCode}'.");
            }

            // The legacy editor encoded one beat as four timing units:
            // 1 = quarter beat, 2 = half beat, 4 = one beat, 8 = two beats.
            var durationTicks = checked((long)Math.Round(
                durationUnits / LegacyUnitsPerBeat * options.Ppq));
            foreach (var character in tokens[0].Distinct())
            {
                if (character == 'P')
                {
                    continue;
                }

                if (!Enum.TryParse<GenshinKey>(character.ToString(), out var key) ||
                    !GenshinKeyMap.TryGetPitch(key, out var pitch))
                {
                    throw CreateFormatException(path, lineIndex, $"Unknown legacy key '{character}'.");
                }

                notes.Add(new NoteEvent
                {
                    Pitch = pitch,
                    StartTick = currentTick,
                    DurationTick = durationTicks,
                    RhythmTick = durationTicks,
                    DurationMode = DurationMode.Auto,
                    Articulation = NoteArticulation.Natural,
                    GateRatio = 0.80,
                    Velocity = 80,
                });
            }

            currentTick = checked(currentTick + durationTicks);
        }

        if (currentTick == 0)
        {
            throw new InvalidDataException($"Legacy score '{path}' contains no musical events.");
        }

        var score = new ScoreDocument
        {
            Metadata = new ScoreMetadata
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Description = "Converted from the legacy .GenshinPiano format.",
            },
            Timing = new TimingDefinition
            {
                Ppq = options.Ppq,
                TempoMap = [new TempoChange { Tick = 0, Bpm = options.Bpm }],
                TimeSignatures = [new TimeSignatureChange()],
            },
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "legacy-main",
                    Name = "Legacy score",
                    Instrument = "windsong-lyre",
                    Notes = notes,
                },
            ],
            Playback = new PlaybackSettings
            {
                Mapping = "genshin-21-key",
                OutOfRangePolicy = OutOfRangePolicy.Reject,
            },
        };

        return NoteDurationCalculator.ApplyAutoDurations(score);
    }

    private static void ValidateOptions(LegacyImportOptions options)
    {
        if (options.Bpm is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "BPM must be between 1 and 1000.");
        }

        if (options.Ppq is < 24 or > 9600)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PPQ must be between 24 and 9600.");
        }
    }

    private static InvalidDataException CreateFormatException(string path, int lineIndex, string message) =>
        new($"{Path.GetFileName(path)}, line {lineIndex + 1}: {message}");
}
