using System.IO;
using System.Xml.Linq;
using GenshinPiano.Application.TextScores;
using GenshinPiano.Core.Scores;
using GenshinPiano.Infrastructure.TextScores;
using GenshinPiano.Infrastructure.Serialization;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class TextScoreImportTests
{
    private readonly TextScoreImporter _importer = new();
    private TextScoreImportResult Parse(string text, TextScoreProfile profile = TextScoreProfile.Zhijian,
        params TextScoreSectionOptions[] sections) => _importer.Parse(text, new() { Profile = profile, Sections = sections });

    [Fact]
    public void RealPairedFiles_PreserveEveryPitchAttackAndEmptyGroup()
    {
        var keys = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "指尖-键盘谱.txt"));
        var numbers = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "指尖-数字谱.txt"));
        var analysis = _importer.Analyze(keys, TextScoreProfile.Zhijian);
        Assert.Equal([8, 8, 48, 14], analysis.Sections.Select(section => section.RowCount));
        Assert.Equal([4, 4, 4, 6], analysis.Sections.Select(section => section.SuggestedGroupsPerRow));
        Assert.Equal(TextScoreNotation.Keys, analysis.Notation);
        Assert.Equal(TextScoreNotation.Numbers, _importer.Analyze(numbers, TextScoreProfile.Zhijian).Notation);
        var a = Parse(keys);
        var b = Parse(numbers);
        Assert.Equal(360, a.AttackCount);
        Assert.Equal(485, a.Score.Tracks.Single().Notes.Count);
        Assert.Equal(329 * 480L, a.EndTick);
        Assert.Equal(a.EndTick, b.EndTick);
        Assert.Equal(a.Score.Tracks.Single().Notes.Select(MusicalEvent), b.Score.Tracks.Single().Notes.Select(MusicalEvent));
        Assert.Empty(ScoreValidator.Validate(a.Score));
        Assert.Single(a.Score.Timing.TimeSignatures); // Six groups do not silently become a new meter.
        Assert.Contains(a.Warnings, warning => warning.Code == "LayoutChange" && warning.Line == 89);
        Assert.Contains(a.Warnings, warning => warning.Code == "TrailingAmbiguous" && warning.Line == 97);
    }

    private static object MusicalEvent(NoteEvent note) => (note.Pitch, note.StartTick, note.RhythmTick, note.DurationTick);

    [Fact]
    public void PrefixAppliesToOneDigit_AndParenthesesAreSimultaneous()
    {
        var a = Parse("+11/-135/(-11+3)");
        var b = Parse("QA/ZDG/(ZAE)");
        Assert.Equal(a.Score.Tracks.Single().Notes.Select(MusicalEvent), b.Score.Tracks.Single().Notes.Select(MusicalEvent));
        Assert.Equal([72, 60, 48, 64, 67, 48, 60, 76], a.Score.Tracks.Single().Notes.Select(note => note.Pitch));
        Assert.Equal([0L, 240L, 480L, 640L, 800L, 960L, 960L, 960L], a.Score.Tracks.Single().Notes.Select(note => note.StartTick));
    }

    [Fact]
    public void EmptyGroupsAdvanceTime_WithoutExtendingThePreviousNote()
    {
        var result = Parse("A/ /D/G");
        var notes = result.Score.Tracks.Single().Notes;
        Assert.Equal([0L, 960L, 1440L], notes.Select(note => note.StartTick));
        Assert.Equal(480, notes[0].RhythmTick);
        Assert.Equal(384, notes[0].DurationTick);
        Assert.Equal(1920, result.EndTick);
    }

    [Fact]
    public void SameText_HasExplicitlyDifferentSlashMeaningByCreator()
    {
        Assert.Equal(960, Parse("A/D", TextScoreProfile.Zhijian).EndTick);
        Assert.Equal(3840, Parse("A/D", TextScoreProfile.Touming).EndTick);
        Assert.Equal(3840, Parse("A|D", TextScoreProfile.Yujun).EndTick);
        Assert.Throws<TextScoreImportException>(() => Parse("A/D", TextScoreProfile.Yujun));
        Assert.Throws<TextScoreImportException>(() => Parse("{AD}", TextScoreProfile.Touming));
        Assert.Throws<TextScoreImportException>(() => Parse("〖AD〗", TextScoreProfile.Yujun));
        Assert.Throws<TextScoreImportException>(() => Parse("[AD]", TextScoreProfile.Zhijian));
    }

    [Theory]
    [InlineData(TextScoreTrailingSlash.Auto, 1920)]
    [InlineData(TextScoreTrailingSlash.Separator, 1920)]
    [InlineData(TextScoreTrailingSlash.Closing, 1440)]
    public void TrailingSlash_IsUserControllable(TextScoreTrailingSlash mode, long expected)
    {
        var result = Parse("A/D/G/", TextScoreProfile.Zhijian,
            new TextScoreSectionOptions { Index = 0, GroupsPerRow = 4, TrailingSlash = mode });
        Assert.Equal(expected, result.EndTick);
        Assert.Equal(3, result.Score.Tracks.Single().Notes.Count);
    }

    [Fact]
    public void XingliClosingSlash_DoesNotCreateAnExtraBeat()
    {
        var result = Parse("QW /(NE) QH /", TextScoreProfile.Xingli);
        Assert.Equal(960, result.EndTick);
        Assert.Equal(6, result.Score.Tracks.Single().Notes.Count);
        Assert.Equal(5, result.AttackCount);
    }

    [Fact]
    public void PerSectionMetersAndGroupLengths_AreAppliedAtActualOffsets()
    {
        var result = Parse("第一段：\nA/D/G/Q\n第四段：\nA/S/D/F/G/H/", TextScoreProfile.Zhijian,
            new() { Index = 0 },
            new() { Index = 1, Numerator = 6, Denominator = 8, GroupDenominator = 8 });
        Assert.Equal(3360, result.EndTick);
        var meter = result.Score.Timing.TimeSignatures[1];
        Assert.Equal((1920L, 6, 8), (meter.Tick, meter.Numerator, meter.Denominator));
        Assert.Equal(240, result.Score.Tracks.Single().Notes[4].RhythmTick);
    }

    [Fact]
    public void ShortRowsContinue_NoImplicitPaddingOrNotesDropped()
    {
        var result = Parse("A/S/D/\nF/G/H/", TextScoreProfile.Zhijian,
            new TextScoreSectionOptions { Index = 0, GroupsPerRow = 6 });
        Assert.Equal(2880, result.EndTick);
        Assert.Equal([0L, 480L, 960L, 1440L, 1920L, 2400L], result.Score.Tracks.Single().Notes.Select(note => note.StartTick));
        Assert.Contains(result.Warnings, warning => warning.Code == "RowWidth");
    }

    [Fact]
    public void InternalWhitespaceSlots_AreOptionalAndDoNotCountPaddingCharacters()
    {
        var result = _importer.Parse("  A   D  /G", new() { Profile = TextScoreProfile.Zhijian, SpacesAreRests = true });
        Assert.Equal([0L, 320L, 480L], result.Score.Tracks.Single().Notes.Select(note => note.StartTick));
        Assert.Equal([160L, 160L, 480L], result.Score.Tracks.Single().Notes.Select(note => note.RhythmTick!.Value));
        Assert.Equal(960, result.EndTick);
        Assert.Equal(240, Parse("  A   D  /G").Score.Tracks.Single().Notes[1].StartTick);
    }

    [Theory]
    [InlineData(TextScoreProfile.Xingli, "[QWE]", 480)]
    [InlineData(TextScoreProfile.Touming, "[QWE]", 1920)]
    public void RapidSequence_StaysInsideItsAllocatedSlot(TextScoreProfile profile, string text, long length)
    {
        var result = Parse(text, profile);
        var notes = result.Score.Tracks.Single().Notes;
        Assert.Equal(3, notes.Count);
        Assert.Equal(length / 3, notes[1].StartTick);
        Assert.Equal(length, notes[^1].StartTick + notes[^1].RhythmTick);
        Assert.Contains(result.Warnings, warning => warning.Code == "Ornament");
    }

    [Theory]
    [InlineData(TextScoreProfile.Yujun, "{ZDG}")]
    [InlineData(TextScoreProfile.Touming, "〖ZDG〗")]
    public void Arpeggios_StaggerWithinTheSlotAndShareRhythmicEnd(TextScoreProfile profile, string text)
    {
        var notes = Parse(text, profile).Score.Tracks.Single().Notes;
        Assert.Equal([0L, 128L, 256L], notes.Select(note => note.StartTick));
        Assert.All(notes, note => Assert.Equal(1920, note.StartTick + note.RhythmTick));
    }

    [Fact]
    public void RoundingUsesBoundaries_AndDoesNotAccumulateDrift()
    {
        var notes = Parse("QWERTYU/A").Score.Tracks.Single().Notes;
        Assert.Equal(480, notes[6].StartTick + notes[6].RhythmTick);
        Assert.Equal(480, notes.Take(7).Sum(note => note.RhythmTick));
        Assert.Equal(480, notes[7].StartTick);
    }

    [Fact]
    public void BomFullWidthSymbolsLowerCaseAndExplicitRows_AreAccepted()
    {
        var a = Parse("\uFEFF（ad）／q／g\\\nf／g｜d");
        var b = Parse("(AD)/Q/G\nF/G\nD");
        Assert.Equal(a.Score.Tracks.Single().Notes.Select(MusicalEvent), b.Score.Tracks.Single().Notes.Select(MusicalEvent));
        Assert.Equal(Parse("（－１＋３）／１").Score.Tracks.Single().Notes.Select(MusicalEvent),
            Parse("(-1+3)/1").Score.Tracks.Single().Notes.Select(MusicalEvent));
    }

    [Theory]
    [InlineData("A 4", "MixedNotation")]
    [InlineData("A/1", "MixedNotation")]
    [InlineData("A/0", "Symbol")]
    [InlineData("8", "Symbol")]
    [InlineData("(AD", "Bracket")]
    [InlineData("()", "Bracket")]
    [InlineData("(AA)", "Duplicate")]
    [InlineData("-", "Symbol")]
    [InlineData("+8", "Symbol")]
    [InlineData("A-", "Symbol")]
    [InlineData("A#", "Symbol")]
    [InlineData("A/D/这里不是音符/G", "Symbol")]
    [InlineData("这里不是音符/A/D", "Symbol")]
    [InlineData("(DAOKO)", "Symbol")]
    public void UnsupportedOrMixedSyntax_IsRejectedWithoutPartialImport(string text, string code)
    {
        var ex = Assert.Throws<TextScoreImportException>(() => Parse(text));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public void SourceErrorsIncludeTheOriginalLineAndColumn()
    {
        var ex = Assert.Throws<TextScoreImportException>(() => _importer.Analyze("第一段：\n A/D/@", TextScoreProfile.Zhijian));
        Assert.Equal((2, 6, "@"), (ex.Line, ex.Column, ex.Detail));
    }

    [Fact]
    public void SongLabelsCreditsAndCopiedFormatting_PreserveTheMusicalRows()
    {
        const string source = """
            前奏：\
            \
            (VAW) E /T Q /W (BSE) /T Q /\
            \
            主歌：（DAOKO）\
            A段：\
            (VAH)  /Q W /  (BSJ) /G F /\
            说明：第1遍使用 BPM 120，/ 用于分组
            B段：\
            N (DJ) /(DH) J /Q (BAJ) /G H /\
            副歌：（DAOKO）\
            &#x20;   /    /    /E TE /\
            间奏：\
            (VS) D /G A /S (BD) /G A /\
            C段：（米津玄师、DAOKO）\
            (VE) (AW)Q /(AFQ) HJ /Q (BSJ)H /(SG) (BD)G /\
            D段：（米津玄师、DAOKO）\
            (ZVH) A /(AFH)HQ(ZV) /W (XB) /(SGE) (BQ) /\
            结尾：\
            W E /T Q /    /    /\
            """;
        const string clean = """
            (VAW) E /T Q /W (BSE) /T Q /
            (VAH)  /Q W /  (BSJ) /G F /
            N (DJ) /(DH) J /Q (BAJ) /G H /
                /    /    /E TE /
            (VS) D /G A /S (BD) /G A /
            (VE) (AW)Q /(AFQ) HJ /Q (BSJ)H /(SG) (BD)G /
            (ZVH) A /(AFH)HQ(ZV) /W (XB) /(SGE) (BQ) /
            W E /T Q /    /    /
            """;
        var analysis = _importer.Analyze(source, TextScoreProfile.Zhijian);
        Assert.Equal(8, analysis.Sections.Count);
        Assert.Equal("主歌：（DAOKO） · A段：", analysis.Sections[1].Name);
        Assert.Equal("C段：（米津玄师、DAOKO）", analysis.Sections[5].Name);
        Assert.All(analysis.Sections, section => Assert.Equal(1, section.RowCount));
        var result = Parse(source);
        var expected = Parse(clean);
        Assert.Equal(expected.EndTick, result.EndTick);
        Assert.Equal(expected.Score.Tracks.Single().Notes.Select(MusicalEvent), result.Score.Tracks.Single().Notes.Select(MusicalEvent));
        Assert.Contains(result.Warnings, warning => warning.Code == "IgnoredText" && warning.Line == 8);
        var detection = _importer.Detect(source);
        Assert.Null(detection.CertainProfile); // Labels and credits cannot establish slash timing semantics.
        Assert.Equal([TextScoreProfile.Zhijian, TextScoreProfile.Xingli, TextScoreProfile.Touming], detection.Candidates);
    }

    [Theory]
    [InlineData(TextScoreProfile.Zhijian, "A/D/G/Q/")]
    [InlineData(TextScoreProfile.Xingli, "A/D/G/Q/")]
    [InlineData(TextScoreProfile.Yujun, "A|D|G|Q|")]
    [InlineData(TextScoreProfile.Touming, "A/D/G/Q/")]
    public void CommentsEnglishLabelsAndBreakOnlyLines_AreCompatibleAcrossProfiles(TextScoreProfile profile, string row)
    {
        var source = "Title: Demo 2026\nIntro:\\\n\\\n" + row + "\\\n" +
            "这段稍微慢一点（重复2次）\nNote: A/D is an example, not an additional score row.\nVerse 1: (Singer)\n" + row;
        var analysis = _importer.Analyze(source, profile);
        Assert.Equal(TextScoreNotation.Keys, analysis.Notation);
        Assert.Equal(2, analysis.Sections.Count);
        var result = Parse(source, profile);
        Assert.Equal(Parse(row + "\n" + row, profile).Score.Tracks.Single().Notes.Select(MusicalEvent),
            result.Score.Tracks.Single().Notes.Select(MusicalEvent));
        Assert.Equal(3, result.Warnings.Count(warning => warning.Code == "IgnoredText"));
    }

    [Theory]
    [InlineData("&#x20;")]
    [InlineData("&#32;")]
    [InlineData("&nbsp;")]
    [InlineData("&#160;")]
    [InlineData("&#xA0;")]
    public void HtmlSpaces_PreserveEmptyGroupsRestSlotsAndOriginalErrorColumns(string entity)
    {
        var options = new TextScoreImportOptions { Profile = TextScoreProfile.Zhijian, SpacesAreRests = true };
        var actual = _importer.Parse("A" + entity + "D/" + entity + "/G/Q/", options);
        var expected = _importer.Parse("A D/ /G/Q/", options);
        Assert.Equal(expected.EndTick, actual.EndTick);
        Assert.Equal(expected.Score.Tracks.Single().Notes.Select(MusicalEvent), actual.Score.Tracks.Single().Notes.Select(MusicalEvent));
        var ex = Assert.Throws<TextScoreImportException>(() => _importer.Analyze("说明：测试\n前奏：\\\n\\\nA/" + entity + "@", TextScoreProfile.Zhijian));
        Assert.Equal((4, entity.Length + 3, "@"), (ex.Line, ex.Column, ex.Detail));
    }

    [Fact]
    public void NotesAloneCannotImport_AndSkippedTextWarningsStayBounded()
    {
        Assert.Equal("Empty", Assert.Throws<TextScoreImportException>(() => Parse("曲名：任意\n前奏：\\\n\\\n备注：没有谱文")).Code);
        var result = Parse(string.Concat(Enumerable.Repeat("备注：测试\n", 500)) + "A/D/G/Q");
        Assert.Equal(201, result.Warnings.Count); // 200 visible warnings plus the omission summary.
        Assert.Equal("301", result.Warnings.Single(warning => warning.Code == "More").Detail);
    }

    [Fact]
    public void ProfileAndNotationAreNeverSilentlyOverridden()
    {
        Assert.Equal("Profile", Assert.Throws<TextScoreImportException>(() => _importer.Parse("AD", new())).Code);
        Assert.Equal("Notation", Assert.Throws<TextScoreImportException>(() => _importer.Parse("+11", new()
            { Profile = TextScoreProfile.Zhijian, Notation = TextScoreNotation.Keys })).Code);
    }

    [Theory]
    [InlineData("{ZDG}|A|", TextScoreProfile.Yujun, TextScoreNotation.Keys)]
    [InlineData("｛－１３５｝｜１｜", TextScoreProfile.Yujun, TextScoreNotation.Numbers)]
    [InlineData("〖ZDG〗/A/", TextScoreProfile.Touming, TextScoreNotation.Keys)]
    public void UniqueCompleteGrammar_CanBeDetected(string text, TextScoreProfile profile, TextScoreNotation notation)
    {
        var detection = _importer.Detect(text);
        Assert.Equal(profile, detection.CertainProfile);
        Assert.Equal(notation, detection.Notation);
        Assert.Equal([profile], detection.Candidates);
        Assert.Equal("UniqueGrammar", detection.EvidenceCode);
    }

    [Theory]
    [InlineData("(AD)/G/Q/G")]
    [InlineData("+11/-135/(-11+3)")]
    [InlineData("[QWE]/(AD)/")]
    [InlineData("A|D")]
    [InlineData("ADG")]
    public void SharedSyntax_NeverSilentlyChoosesItsTimingMeaning(string text)
    {
        var detection = _importer.Detect(text);
        Assert.Null(detection.CertainProfile);
        Assert.True(detection.Candidates.Count > 1);
        Assert.Equal("SharedSymbols", detection.EvidenceCode);
    }

    [Fact]
    public void RealSampleLayout_OnlySuggestsZhijianInsteadOfClaimingCertainty()
    {
        foreach (var name in new[] { "指尖-键盘谱.txt", "指尖-数字谱.txt" })
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));
            var detection = _importer.Detect(text);
            Assert.Null(detection.CertainProfile);
            Assert.Equal(TextScoreProfile.Zhijian, detection.SuggestedProfile);
            Assert.Equal("FourGroupRows", detection.EvidenceCode);
            Assert.Equal([TextScoreProfile.Zhijian, TextScoreProfile.Xingli, TextScoreProfile.Touming], detection.Candidates);
        }
    }

    [Theory]
    [InlineData("{AD}|G|@", "NoFormat")]
    [InlineData("〖AD〗/Q/{DG}", "NoFormat")]
    [InlineData("[QWE", "NoFormat")]
    [InlineData("A/1", "MixedNotation")]
    [InlineData("", "Empty")]
    public void DetectionRejectsInvalidSuffixesMixedConventionsAndMixedVersions(string text, string code)
    {
        Assert.Equal(code, Assert.Throws<TextScoreImportException>(() => _importer.Detect(text)).Code);
    }

    [Fact]
    public void InvalidSettingsOversizeInputAndTooDenseEvents_AreRejected()
    {
        Assert.Throws<TextScoreImportException>(() => Parse("A", TextScoreProfile.Zhijian, new TextScoreSectionOptions { Denominator = 3 }));
        Assert.Throws<TextScoreImportException>(() => _importer.Parse("A", new() { Profile = TextScoreProfile.Zhijian, Bpm = double.NaN }));
        Assert.Throws<TextScoreImportException>(() => Parse(new string('A', TextScoreImportLimits.MaxTextLength + 1)));
        Assert.Throws<TextScoreImportException>(() => Parse(new string('A', 61), TextScoreProfile.Zhijian, new TextScoreSectionOptions { GroupDenominator = 32 }));
        var manySections = string.Join("\n", Enumerable.Range(1, 129).Select(i => $"Section {i}:\nA"));
        Assert.Equal("Limit", Assert.Throws<TextScoreImportException>(() => _importer.Analyze(manySections, TextScoreProfile.Zhijian)).Code);
    }

    [Fact]
    public async Task ImportedResult_RoundTripsThroughExistingGpianoSerializer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GPiano-TextRoundTrip-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "converted.gpiano");
        try
        {
            var result = Parse("第一段：\n(AD)/G/ /Q\n第二段：\n(QZA)/S/D/", TextScoreProfile.Zhijian);
            var serializer = new JsonScoreDocumentSerializer();
            await serializer.SaveAsync(result.Score, file);
            var restored = await serializer.LoadAsync(file);
            Assert.Equal(result.Score.Tracks.Single().Notes.Select(MusicalEvent), restored.Tracks.Single().Notes.Select(MusicalEvent));
            Assert.Equal(result.Score.Metadata.Title, restored.Metadata.Title);
            Assert.All(restored.Tracks.Single().Notes, note => Assert.Equal(DurationMode.Auto, note.DurationMode));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void PreviewWarningsAndErrorsHaveBothLanguageResources()
    {
        var keys = new[] { "Profile", "Options", "Empty", "Limit", "MixedNotation", "Notation", "Symbol", "Bracket", "Duplicate", "Precision", "Encoding", "File", "NoFormat" };
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", $"Strings.{language}.xaml"));
            var resources = xml.Root!.Elements().ToDictionary(element => (string)element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, element => element.Value);
            foreach (var code in keys) Assert.True(resources.ContainsKey("TextImport_Error_" + code), code);
            foreach (var code in new[] { "EstimatedTiming", "LayoutChange", "RowWidth", "TrailingAmbiguous", "MeterBoundary", "Ornament", "TrailingRest", "More", "IgnoredText" })
                Assert.False(string.IsNullOrWhiteSpace(string.Format(resources["TextImport_Warning_" + code], 1, "2")));
        }
    }
}
