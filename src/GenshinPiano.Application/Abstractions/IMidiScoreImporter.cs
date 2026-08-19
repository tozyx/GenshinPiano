using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Abstractions;

public sealed record MidiImportOptions(
    bool IgnorePercussion = true,
    OutOfRangePolicy OutOfRangePolicy = OutOfRangePolicy.OctaveFold,
    int Transpose = 0,
    IReadOnlyCollection<int>? TrackIndices = null);

public sealed record MidiTrackInfo(
    int Index,
    string Name,
    int NoteCount,
    int PercussionNoteCount,
    int? MinimumPitch,
    int? MaximumPitch);

public sealed record MidiFileInfo(string FileName, IReadOnlyList<MidiTrackInfo> Tracks);

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
    Task<MidiFileInfo> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<MidiImportResult> ImportAsync(
        string path,
        MidiImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
