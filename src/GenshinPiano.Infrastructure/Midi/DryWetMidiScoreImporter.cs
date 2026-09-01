using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using ScoreNoteEvent = GenshinPiano.Core.Scores.NoteEvent;

namespace GenshinPiano.Infrastructure.Midi;

public sealed class DryWetMidiScoreImporter : IMidiScoreImporter
{
    private const int MinimumPpq = 24;
    private const int MaximumPpq = 9600;
    private const int PercussionChannel = 9;

    public Task<MidiFileInfo> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var midiFile = MidiFile.Read(path);
            var tracks = midiFile.GetTrackChunks().Select((chunk, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var notes = chunk.GetNotes().ToArray();
                var pitchedNotes = notes.Where(note => (int)note.Channel != PercussionChannel).ToArray();
                var name = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
                return new MidiTrackInfo(
                    index,
                    string.IsNullOrWhiteSpace(name) ? $"MIDI Track {index + 1}" : name,
                    notes.Length,
                    notes.Count(note => (int)note.Channel == PercussionChannel),
                    pitchedNotes.Length == 0 ? null : pitchedNotes.Min(note => (int)note.NoteNumber),
                    pitchedNotes.Length == 0 ? null : pitchedNotes.Max(note => (int)note.NoteNumber));
            }).ToArray();
            return new MidiFileInfo(Path.GetFileName(path), tracks);
        }, cancellationToken);
    }

    public Task<MidiImportResult> ImportAsync(
        string path,
        MidiImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new MidiImportOptions();
        return Task.Run(() => Import(path, options, cancellationToken), cancellationToken);
    }

    private static MidiImportResult Import(
        string path,
        MidiImportOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var midiFile = MidiFile.Read(path);
        if (midiFile.TimeDivision is not TicksPerQuarterNoteTimeDivision timeDivision)
        {
            throw new NotSupportedException("SMPTE time-division MIDI files are not supported yet.");
        }

        var sourcePpq = (int)timeDivision.TicksPerQuarterNote;
        var targetPpq = Math.Clamp(sourcePpq, MinimumPpq, MaximumPpq);
        var trackChunks = midiFile.GetTrackChunks().ToArray();
        var scoreTracks = new List<ScoreTrack>();
        var importedNotes = 0;
        var foldedNotes = 0;
        var droppedNotes = 0;
        var ignoredPercussionNotes = 0;
        var selectedTracks = options.TrackIndices is null
            ? null
            : options.TrackIndices.ToHashSet();

        for (var trackIndex = 0; trackIndex < trackChunks.Length; trackIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selectedTracks is not null && !selectedTracks.Contains(trackIndex))
            {
                continue;
            }
            var chunk = trackChunks[trackIndex];
            var notes = new List<ScoreNoteEvent>();
            foreach (var midiNote in chunk.GetNotes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options.IgnorePercussion && (int)midiNote.Channel == PercussionChannel)
                {
                    ignoredPercussionNotes++;
                    continue;
                }

                var sourcePitch = checked((int)midiNote.NoteNumber + Math.Clamp(options.Transpose, -36, 36));
                int pitch;
                bool folded;
                if (options.PreserveOriginalPitch)
                {
                    if (sourcePitch is < 0 or > 127)
                    {
                        droppedNotes++;
                        continue;
                    }
                    pitch = sourcePitch;
                    folded = false;
                }
                else if (!TryMapPitch(sourcePitch, options.OutOfRangePolicy, out pitch, out folded))
                {
                    droppedNotes++;
                    continue;
                }

                if (folded)
                {
                    foldedNotes++;
                }

                notes.Add(new ScoreNoteEvent
                {
                    Pitch = pitch,
                    StartTick = ScaleTick(midiNote.Time, sourcePpq, targetPpq),
                    DurationTick = Math.Max(1, ScaleTick(midiNote.Length, sourcePpq, targetPpq)),
                    DurationMode = DurationMode.Explicit,
                    Velocity = Math.Clamp((int)midiNote.Velocity, 1, 127),
                });
                importedNotes++;
            }

            if (notes.Count == 0)
            {
                continue;
            }

            var trackName = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
            var program = chunk.Events.OfType<ProgramChangeEvent>().FirstOrDefault();
            scoreTracks.Add(new ScoreTrack
            {
                Id = $"midi-{trackIndex + 1}",
                Name = string.IsNullOrWhiteSpace(trackName) ? $"MIDI Track {trackIndex + 1}" : trackName,
                Instrument = program is null ? "midi" : $"midi-program-{(int)program.ProgramNumber}",
                Notes = notes.OrderBy(note => note.StartTick).ThenBy(note => note.Pitch).ToList(),
            });
        }

        if (scoreTracks.Count == 0)
        {
            throw new InvalidDataException("The MIDI file contains no notes that can be mapped to the Genshin 21-key range.");
        }

        var tempoMap = midiFile.GetTempoMap();
        var tempos = tempoMap.GetTempoChanges()
            .Select(change => new TempoChange
            {
                Tick = ScaleTick(change.Time, sourcePpq, targetPpq),
                Bpm = change.Value.BeatsPerMinute,
            })
            .GroupBy(change => change.Tick)
            .Select(group => group.Last())
            .OrderBy(change => change.Tick)
            .ToList();
        if (tempos.Count == 0 || tempos[0].Tick != 0)
        {
            tempos.Insert(0, new TempoChange());
        }

        var timeSignatures = tempoMap.GetTimeSignatureChanges()
            .Select(change => new TimeSignatureChange
            {
                Tick = ScaleTick(change.Time, sourcePpq, targetPpq),
                Numerator = change.Value.Numerator,
                Denominator = change.Value.Denominator,
            })
            .GroupBy(change => change.Tick)
            .Select(group => group.Last())
            .OrderBy(change => change.Tick)
            .ToList();
        if (timeSignatures.Count == 0 || timeSignatures[0].Tick != 0)
        {
            timeSignatures.Insert(0, new TimeSignatureChange());
        }

        var score = new ScoreDocument
        {
            Metadata = new ScoreMetadata
            {
                Title = Path.GetFileNameWithoutExtension(path),
                Description = $"Imported from MIDI file: {Path.GetFileName(path)}",
            },
            Timing = new TimingDefinition
            {
                Ppq = targetPpq,
                TempoMap = tempos,
                TimeSignatures = timeSignatures,
            },
            Tracks = scoreTracks,
            Playback = new PlaybackSettings
            {
                Mapping = "genshin-21-key",
                OutOfRangePolicy = options.PreserveOriginalPitch
                    ? OutOfRangePolicy.Drop
                    : OutOfRangePolicy.Reject,
            },
        };

        return new MidiImportResult(
            score,
            new MidiImportReport(
                trackChunks.Length,
                scoreTracks.Count,
                importedNotes,
                foldedNotes,
                droppedNotes,
                ignoredPercussionNotes));
    }

    private static bool TryMapPitch(
        int sourcePitch,
        OutOfRangePolicy policy,
        out int mappedPitch,
        out bool folded)
    {
        mappedPitch = sourcePitch;
        folded = false;
        if (policy == OutOfRangePolicy.OctaveFold)
        {
            while (mappedPitch < 48)
            {
                mappedPitch += 12;
                folded = true;
            }

            while (mappedPitch > 83)
            {
                mappedPitch -= 12;
                folded = true;
            }
        }

        if (mappedPitch is < 48 or > 83 ||
            !GenshinKeyMap.TryMapPitch(mappedPitch, 0, OutOfRangePolicy.Reject, out _))
        {
            if (policy == OutOfRangePolicy.Reject)
            {
                throw new InvalidDataException($"MIDI pitch {sourcePitch} cannot be mapped to a Genshin key.");
            }

            return false;
        }

        return true;
    }

    private static long ScaleTick(long tick, int sourcePpq, int targetPpq) =>
        checked((long)Math.Round(tick * (double)targetPpq / sourcePpq));
}
