namespace GenshinPiano.Core.Scores;

public sealed record ScoreDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ScoreMetadata Metadata { get; init; } = new();

    public TimingDefinition Timing { get; init; } = new();

    public List<ScoreTrack> Tracks { get; init; } = [];

    public PlaybackSettings Playback { get; init; } = new();

    public static ScoreDocument CreateEmpty(string title = "未命名曲谱") => new()
    {
        Metadata = new ScoreMetadata { Title = title },
        Tracks =
        [
            new ScoreTrack
            {
                Id = "main",
                Name = "主音轨",
                Instrument = "windsong-lyre",
            },
        ],
    };
}

public sealed record ScoreMetadata
{
    public string Title { get; init; } = "未命名曲谱";

    public string Author { get; init; } = string.Empty;

    public string Arranger { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}

public sealed record TimingDefinition
{
    public int Ppq { get; init; } = 480;

    public List<TempoChange> TempoMap { get; init; } = [new()];

    public List<TimeSignatureChange> TimeSignatures { get; init; } = [new()];
}

public sealed record TempoChange
{
    public long Tick { get; init; }

    public double Bpm { get; init; } = 120;
}

public sealed record TimeSignatureChange
{
    public long Tick { get; init; }

    public int Numerator { get; init; } = 4;

    public int Denominator { get; init; } = 4;
}

public sealed record ScoreTrack
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "音轨";

    public string Instrument { get; init; } = "windsong-lyre";

    public bool IsMuted { get; init; }

    public List<NoteEvent> Notes { get; init; } = [];
}

public sealed record NoteEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public int Pitch { get; init; }

    public long StartTick { get; init; }

    public long DurationTick { get; init; }

    public long? RhythmTick { get; init; }

    public DurationMode DurationMode { get; init; } = DurationMode.Explicit;

    public NoteArticulation Articulation { get; init; } = NoteArticulation.Natural;

    public double? GateRatio { get; init; }

    public int Velocity { get; init; } = 80;
}

public enum DurationMode
{
    Explicit,
    Auto,
}

public enum NoteArticulation
{
    Natural,
    Legato,
    Detached,
    Staccato,
    Custom,
}

public sealed record PlaybackSettings
{
    public int Transpose { get; init; }

    public string Mapping { get; init; } = "genshin-21-key";

    public OutOfRangePolicy OutOfRangePolicy { get; init; } = OutOfRangePolicy.OctaveFold;
}

public enum OutOfRangePolicy
{
    Reject,
    Drop,
    OctaveFold,
}
