using GenshinPiano.Infrastructure.Midi;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class MidiImportTests
{
    [Fact]
    public async Task Importer_PreservesTimingAndFoldsPitchWhileIgnoringPercussion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mid");
        try
        {
            var melodyOn = new NoteOnEvent((SevenBitNumber)36, (SevenBitNumber)96);
            var melodyOff = new NoteOffEvent((SevenBitNumber)36, (SevenBitNumber)0) { DeltaTime = 480 };
            var percussionOn = new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100)
            {
                Channel = (FourBitNumber)9,
            };
            var percussionOff = new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0)
            {
                Channel = (FourBitNumber)9,
                DeltaTime = 120,
            };
            var track = new TrackChunk(
                new SequenceTrackNameEvent("Lead"),
                new SetTempoEvent(600_000),
                melodyOn,
                melodyOff,
                percussionOn,
                percussionOff);
            var midiFile = new MidiFile(track)
            {
                TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
            };
            midiFile.Write(path, overwriteFile: true);

            var result = await new DryWetMidiScoreImporter().ImportAsync(path);

            var note = Assert.Single(Assert.Single(result.Score.Tracks).Notes);
            Assert.Equal(48, note.Pitch);
            Assert.Equal(480, note.DurationTick);
            Assert.Equal(96, note.Velocity);
            Assert.Equal(100, result.Score.Timing.TempoMap[0].Bpm, precision: 6);
            Assert.Equal(1, result.Report.FoldedNoteCount);
            Assert.Equal(1, result.Report.IgnoredPercussionNoteCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
