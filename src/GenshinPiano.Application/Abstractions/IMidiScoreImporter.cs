using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Abstractions;

public sealed record MidiImportOptions(
    bool IgnorePercussion = true,
    OutOfRangePolicy OutOfRangePolicy = OutOfRangePolicy.OctaveFold);

public sealed record MidiImportReport(
    int SourceTrackCount,
    int ImportedTrackCount,
    int ImportedNoteCount,
    int FoldedNoteCount,
    int DroppedNoteCount,
    int IgnoredPercussionNoteCount);

public sealed record MidiImportResult(ScoreDocument Score, MidiImportReport Report);

public interface IMidiScoreImporter
{
    Task<MidiImportResult> ImportAsync(
        string path,
        MidiImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
