using System.Globalization;
using System.Text.Json.Nodes;
using GenshinPiano.Application.AiScores;
using GenshinPiano.Infrastructure.AiScores;
using GenshinPiano.Infrastructure.Serialization;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class AiScoreImportTests
{
    private readonly AiScoreJsonImporter _importer = new();

    private static JsonObject Example() => JsonNode.Parse(AiScorePrompt.ExampleJson)!.AsObject();
    private static JsonObject Measure(JsonObject root) => root["measures"]![0]!.AsObject();

    private AiScoreImportResult Notes(string notes, string? length = null, int numerator = 4, int denominator = 4)
    {
        var root = Example();
        root["timeSignature"] = new JsonArray(numerator, denominator);
        Measure(root)["notes"] = JsonNode.Parse(notes);
        if (length is not null) Measure(root)["length"] = length;
        return _importer.Parse(root.ToJsonString());
    }

    [Fact]
    public void PromptExample_PreservesPitchesVoicesRestsAndTiming()
    {
        var result = _importer.Parse(AiScorePrompt.ExampleJson);
        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.MeasureCount);
        Assert.Equal(2, result.Score.Tracks.Count);
        var right = result.Score.Tracks.Single(track => track.Name == "right");
        Assert.Equal([60, 64], right.Notes.Select(note => note.Pitch));
        Assert.Equal([0L, 480L], right.Notes.Select(note => note.StartTick));
        var left = result.Score.Tracks.Single(track => track.Name == "left");
        Assert.Equal(48, left.Notes.Single().Pitch);
        Assert.Equal(1920, left.Notes.Single().DurationTick);
        Assert.All(result.Score.Tracks.SelectMany(track => track.Notes), note => Assert.NotEqual(Guid.Empty, note.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("json")]
    public void AWholeCodeFenceAndBom_AreAccepted(string language)
    {
        var text = "\uFEFF\n```" + language + "\n" + AiScorePrompt.ExampleJson + "\n```";
        Assert.Equal(3, _importer.Parse(text).Score.Tracks.Sum(track => track.Notes.Count));
    }

    [Theory]
    [InlineData("C-1", 0)]
    [InlineData("G9", 127)]
    [InlineData("B#3", 60)]
    [InlineData("Cb4", 59)]
    [InlineData("Bb3", 58)]
    [InlineData("F♯4", 66)]
    [InlineData("C##4", 62)]
    public void ScientificPitch_UsesAbsoluteMidiPitch(string pitch, int expected)
    {
        var root = Example();
        Measure(root)["notes"]![0]!["pitch"] = pitch;
        var result = _importer.Parse(root.ToJsonString());
        Assert.Equal(expected, result.Score.Tracks.Single(track => track.Name == "right").Notes[0].Pitch);
    }

    [Fact]
    public void TripletFractionsAndChords_AreNotSummedAsSequentialDurations()
    {
        var result = Notes("""
            [
              {"voice":"right","pitch":60,"start":"0","duration":"1/3"},
              {"voice":"right","pitch":64,"start":"0","duration":"1/3"},
              {"voice":"right","pitch":62,"start":"1/3","duration":"1/3"},
              {"voice":"right","pitch":64,"start":"2/3","duration":"1/3"},
              {"voice":"right","pitch":null,"start":"1","duration":"3"}
            ]
            """);
        Assert.Single(result.Warnings, warning => warning.Code == "TrailingRest");
        Assert.Equal([0L, 0L, 160L, 320L], result.Score.Tracks.Single().Notes.Select(note => note.StartTick));
        Assert.All(result.Score.Tracks.Single().Notes, note => Assert.Equal(160, note.DurationTick));
    }

    [Fact]
    public void FractionRounding_UsesAbsoluteBoundariesAndWarns()
    {
        var notes = new JsonArray();
        for (var i = 0; i < 7; i++)
            notes.Add(new JsonObject { ["voice"] = "right", ["pitch"] = 60 + i, ["start"] = $"{i}/7", ["duration"] = "1/7" });
        notes.Add(new JsonObject { ["voice"] = "right", ["pitch"] = null, ["start"] = 1, ["duration"] = 3 });
        var result = Notes(notes.ToJsonString());
        var sounding = result.Score.Tracks.Single().Notes;
        Assert.Equal(480, sounding[^1].StartTick + sounding[^1].DurationTick);
        Assert.Equal(480, sounding.Sum(note => note.DurationTick));
        Assert.Single(result.Warnings, warning => warning.Code == "Rounded");
    }

    [Fact]
    public void SixEight_UsesThreeQuarterNotesPerMeasure()
    {
        var result = Notes("""[{"voice":"one","pitch":60,"start":0,"duration":3}]""", numerator: 6, denominator: 8);
        Assert.Empty(result.Warnings);
        Assert.Equal(1440, result.Score.Tracks.Single().Notes.Single().DurationTick);
        Assert.Equal("Bounds", Assert.Throws<AiScoreImportException>(() =>
            Notes("""[{"voice":"one","pitch":60,"start":0,"duration":6}]""", numerator: 6, denominator: 8)).Code);
    }

    [Fact]
    public void PickupAndMeterTempoChanges_AdvanceByActualMeasureLength()
    {
        var root = Example();
        root["measures"] = JsonNode.Parse("""
            [
              {"number":1,"length":"1","notes":[{"voice":"one","pitch":60,"start":0,"duration":1}]},
              {"number":2,"timeSignature":[3,8],"bpm":90,"notes":[{"voice":"one","pitch":62,"start":0,"duration":"3/2"}]}
            ]
            """);
        var result = _importer.Parse(root.ToJsonString());
        Assert.Equal([0L, 480L], result.Score.Tracks.Single().Notes.Select(note => note.StartTick));
        Assert.Equal(480, result.Score.Timing.TimeSignatures[1].Tick);
        Assert.Equal(3, result.Score.Timing.TimeSignatures[1].Numerator);
        Assert.Equal(90, result.Score.Timing.TempoMap[1].Bpm);
        Assert.Single(result.Warnings, warning => warning.Code == "ShortMeasure");
    }

    [Fact]
    public void CrossBarTies_MergeOnlyMatchingContiguousVoiceAndPitch()
    {
        var root = Example();
        root["measures"] = JsonNode.Parse("""
            [
              {"number":1,"notes":[{"voice":"right","pitch":"C4","start":0,"duration":4,"tie":"start"}]},
              {"number":2,"notes":[{"voice":"right","pitch":"C4","start":0,"duration":4,"tie":"continue"}]},
              {"number":3,"notes":[{"voice":"right","pitch":"C4","start":0,"duration":4,"tie":"stop"}]}
            ]
            """);
        var note = _importer.Parse(root.ToJsonString()).Score.Tracks.Single().Notes.Single();
        Assert.Equal(5760, note.DurationTick);
        Assert.Equal(5760, note.RhythmTick);
    }

    [Fact]
    public void VoiceGapsAndModelUncertainty_AreReviewNotesNotSilentRepairs()
    {
        var root = Example();
        Measure(root)["notes"] = JsonNode.Parse("""[{"voice":"right","pitch":60,"start":1,"duration":1,"uncertain":true}]""");
        root["bpm"] = null;
        root["warnings"] = new JsonArray("Unreadable tempo");
        var result = _importer.Parse(root.ToJsonString());
        Assert.Contains(result.Warnings, warning => warning.Code == "VoiceGap");
        Assert.Contains(result.Warnings, warning => warning.Code == "Uncertain");
        Assert.Contains(result.Warnings, warning => warning.Code == "TempoDefault");
        Assert.Contains(result.Warnings, warning => warning.Code == "Model");
        Assert.Equal(480, result.Score.Tracks.Single().Notes.Single().StartTick);
        Assert.Equal(120, result.Score.Timing.TempoMap.Single().Bpm);
    }

    [Fact]
    public void AnAbsentVoice_IsReportedWithMeasureLocation()
    {
        var root = Example();
        root["measures"]!.AsArray().Add(JsonNode.Parse("""{"number":2,"notes":[{"voice":"right","pitch":62,"start":0,"duration":4}]}"""));
        var result = _importer.Parse(root.ToJsonString());
        Assert.Contains(result.Warnings, warning => warning.Code == "MissingVoice" && warning.Location == "$.measures[1]" && warning.Detail == "left");
    }

    [Theory]
    [InlineData("format", "\"other\"", "Format")]
    [InlineData("version", "2", "Version")]
    [InlineData("complete", "false", "Incomplete")]
    [InlineData("complete", "null", "Type")]
    [InlineData("bpm", "0", "Range")]
    [InlineData("bpm", "1001", "Range")]
    [InlineData("timeSignature", "[4,3]", "Range")]
    [InlineData("timeSignature", "[4,4,4]", "Limit")]
    [InlineData("measures", "[]", "Empty")]
    [InlineData("measures", "null", "Type")]
    public void InvalidRootData_IsRejected(string field, string json, string code)
    {
        var root = Example();
        root[field] = JsonNode.Parse(json);
        Assert.Equal(code, Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString())).Code);
    }

    [Theory]
    [InlineData("pitch", "128", "Pitch")]
    [InlineData("pitch", "\"C10\"", "Pitch")]
    [InlineData("pitch", "1.5", "Type")]
    [InlineData("start", "-1", "Bounds")]
    [InlineData("start", "\"1/0\"", "Number")]
    [InlineData("duration", "0", "Bounds")]
    [InlineData("duration", "5", "Bounds")]
    [InlineData("duration", "\"1/100000000000\"", "Precision")]
    [InlineData("tie", "\"stop\"", "Tie")]
    [InlineData("tie", "\"start\"", "Tie")]
    [InlineData("uncertain", "\"true\"", "Type")]
    public void InvalidNotes_ReportTheirLocation(string field, string json, string code)
    {
        var root = Example();
        Measure(root)["notes"]![0]![field] = JsonNode.Parse(json);
        var error = Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString()));
        Assert.Equal(code, error.Code);
        Assert.StartsWith("$.measures", error.Location);
    }

    [Fact]
    public void DuplicatesUnknownFieldsAndMissingFields_AreRejected()
    {
        var duplicateProperty = AiScorePrompt.ExampleJson.Replace("\"version\": 1", "\"version\": 1, \"version\": 1");
        Assert.Equal("Duplicate", Assert.Throws<AiScoreImportException>(() => _importer.Parse(duplicateProperty)).Code);
        var root = Example();
        Measure(root)["notes"]!.AsArray().Add(Measure(root)["notes"]![0]!.DeepClone());
        Assert.Equal("Duplicate", Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString())).Code);
        root = Example();
        Measure(root)["notes"]![0]!["accidental"] = "#";
        Assert.Equal("UnknownField", Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString())).Code);
        root = Example();
        Measure(root)["notes"]![0]!.AsObject().Remove("start");
        Assert.Equal("Required", Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString())).Code);
    }

    [Fact]
    public void MissingMeasuresAndRestOnlyScores_AreRejected()
    {
        var root = Example();
        Measure(root)["number"] = 2;
        Assert.Equal("Numbering", Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString())).Code);
        Assert.Equal("Empty", Assert.Throws<AiScoreImportException>(() =>
            Notes("""[{"voice":"one","pitch":null,"start":0,"duration":4}]""")).Code);
    }

    [Theory]
    [InlineData("Here is your JSON: ")]
    [InlineData("{} ")]
    public void SurroundingProseOrMultipleObjects_AreNotSilentlyExtracted(string prefix) =>
        Assert.Equal("Json", Assert.Throws<AiScoreImportException>(() => _importer.Parse(prefix + AiScorePrompt.ExampleJson)).Code);

    [Fact]
    public async Task ImportedScore_CanBeSavedAndReopenedWithoutTimingLoss()
    {
        var score = _importer.Parse(AiScorePrompt.ExampleJson).Score;
        var path = Path.Combine(Path.GetTempPath(), $"ai-score-{Guid.NewGuid():N}.gpiano");
        try
        {
            var serializer = new JsonScoreDocumentSerializer();
            await serializer.SaveAsync(score, path);
            var loaded = await serializer.LoadAsync(path);
            Assert.Equal(score.Tracks.SelectMany(track => track.Notes).Select(note => (note.Pitch, note.StartTick, note.DurationTick)),
                loaded.Tracks.SelectMany(track => track.Notes).Select(note => (note.Pitch, note.StartTick, note.DurationTick)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NumericTiming_IsIndependentOfUiCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var result = Notes("""[{"voice":"one","pitch":60,"start":0,"duration":0.5},{"voice":"one","pitch":null,"start":0.5,"duration":3.5}]""");
            Assert.Equal(240, result.Score.Tracks.Single().Notes.Single().DurationTick);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void OversizedInput_IsRejectedBeforeJsonParsing() =>
        Assert.Equal("Limit", Assert.Throws<AiScoreImportException>(() => _importer.Parse(new string('x', AiScoreImportLimits.MaxTextLength + 1))).Code);

    [Fact]
    public void FinalSilentMeasure_IsReportedInsteadOfFabricatingANote()
    {
        var root = Example();
        root["measures"] = JsonNode.Parse("""
            [
              {"number":1,"notes":[{"voice":"one","pitch":60,"start":0,"duration":4}]},
              {"number":2,"notes":[{"voice":"one","pitch":null,"start":0,"duration":4}]}
            ]
            """);
        var result = _importer.Parse(root.ToJsonString());
        Assert.Equal(2, result.MeasureCount);
        Assert.Equal(1920, result.Score.Tracks.Single().Notes.Single().DurationTick);
        Assert.Single(result.Warnings, warning => warning.Code == "TrailingRest");
    }

    [Fact]
    public void ExcessiveFractionComplexity_IsBounded()
    {
        var root = Example();
        var measures = new JsonArray();
        for (var index = 0; index < 200; index++)
        {
            var denominator = System.Numerics.BigInteger.Pow(10, 25) + index * 2 + 1;
            measures.Add(new JsonObject
            {
                ["number"] = index + 1,
                ["length"] = $"{denominator + 1}/{denominator}",
                ["notes"] = new JsonArray(
                    new JsonObject { ["voice"] = "one", ["pitch"] = 60, ["start"] = 0, ["duration"] = 1 },
                    new JsonObject { ["voice"] = "one", ["pitch"] = null, ["start"] = 1, ["duration"] = $"1/{denominator}" }),
            });
        }
        root["measures"] = measures;
        Assert.Equal("Limit", Assert.Throws<AiScoreImportException>(() => _importer.Parse(root.ToJsonString())).Code);
    }
}
