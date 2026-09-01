using System.Xml;
using GenshinPiano.Infrastructure.MusicXml;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class MusicXmlImportTests
{
    [Fact]
    public async Task PreservesPickupAndLateEnteringLowerStaff()
    {
        const string xml = """
            <score-partwise version="4.0">
              <work><work-title>Piano Test</work-title></work>
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">
                <measure number="0" implicit="yes">
                  <attributes><divisions>4</divisions><time><beats>4</beats><beat-type>4</beat-type></time><staves>2</staves></attributes>
                  <note><pitch><step>G</step><octave>4</octave></pitch><duration>4</duration><voice>1</voice><staff>1</staff></note>
                </measure>
                <measure number="1">
                  <note><pitch><step>C</step><octave>5</octave></pitch><duration>16</duration><voice>1</voice><staff>1</staff></note>
                  <backup><duration>16</duration></backup>
                  <note><pitch><step>C</step><octave>3</octave></pitch><duration>16</duration><voice>2</voice><staff>2</staff></note>
                </measure>
              </part>
            </score-partwise>
            """;

        var result = await ImportAsync(xml);

        Assert.Equal("Piano Test", result.Score.Metadata.Title);
        Assert.Equal(2, result.Score.Tracks.Count);
        var upper = result.Score.Tracks.Single(x => x.Name.EndsWith("Staff 1"));
        var lower = result.Score.Tracks.Single(x => x.Name.EndsWith("Staff 2"));
        Assert.Equal([0L, 480L], upper.Notes.Select(x => x.StartTick));
        Assert.Equal(480, Assert.Single(lower.Notes).StartTick);
    }

    [Fact]
    public async Task MergesTiesAndPreservesChordTempoAndAccidental()
    {
        const string xml = """
            <score-partwise version="4.0">
              <part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>2</divisions></attributes><direction><sound tempo="96"/></direction>
                  <note><pitch><step>F</step><alter>1</alter><octave>4</octave></pitch><duration>2</duration><voice>1</voice><tie type="start"/></note>
                  <note><chord/><pitch><step>A</step><octave>4</octave></pitch><duration>2</duration><voice>1</voice></note>
                </measure>
                <measure number="2">
                  <note><pitch><step>F</step><alter>1</alter><octave>4</octave></pitch><duration>2</duration><voice>1</voice><tie type="stop"/></note>
                </measure>
              </part>
            </score-partwise>
            """;

        var result = await ImportAsync(xml);
        var notes = Assert.Single(result.Score.Tracks).Notes;

        Assert.Equal(2, notes.Count);
        Assert.Equal(960, notes.Single(x => x.Pitch == 66).DurationTick);
        Assert.All(notes, x => Assert.Equal(0, x.StartTick));
        Assert.Equal(96, result.Score.Timing.TempoMap[0].Bpm);
        Assert.Equal(1, result.Report.ChromaticNoteCount);
    }

    [Fact]
    public async Task RejectsExternalDtd()
    {
        const string xml = """
            <!DOCTYPE score-partwise SYSTEM "file:///C:/Windows/win.ini">
            <score-partwise version="4.0"/>
            """;
        await Assert.ThrowsAnyAsync<XmlException>(() => ImportAsync(xml));
    }

    private static async Task<MusicXmlImportResult> ImportAsync(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.musicxml");
        try
        {
            await File.WriteAllTextAsync(path, xml);
            return await new MusicXmlScoreImporter().ImportAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
