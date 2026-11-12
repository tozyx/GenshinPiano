using System.Xml.Linq;
using GenshinPiano.App.Services;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class OcrWarningLocalizerTests
{
    private static Dictionary<string, string> Resources(string language)
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestData", $"Strings.{language}.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!.Elements().ToDictionary(element => (string)element.Attribute(xaml + "Key")!, element => element.Value);
    }

    [Theory]
    [InlineData("Accompaniment voices were detected but excluded from the imported score.", "Ocr_Warning_AccompanimentExcluded")]
    [InlineData("Multiple numbered-notation voices were inferred from the page layout. Review their alignment.", "Ocr_Warning_MultipleVoices")]
    [InlineData("A single numbered-notation voice was inferred from the page layout.", "Ocr_Warning_SingleVoice")]
    [InlineData("Octave dots, rhythm underlines and augmentation dots were inferred geometrically. Review low-confidence symbols before playback.", "Ocr_Warning_Geometry")]
    [InlineData("Recognized accidentals and extension dashes were preserved. Review curved ties and other low-confidence notation marks.", "Ocr_Warning_AccidentalsAndTies")]
    [InlineData("Tempo was not recognized; the initial tempo is 120 BPM. Adjust it after importing.", "Ocr_Warning_TempoDefault")]
    [InlineData("No initial time signature was recognized; 4/4 is used for display. Review the time signature.", "Ocr_Warning_TimeSignatureDefault")]
    [InlineData("The image ends with open ties; review the ending durations.", "Ocr_Warning_OpenTies")]
    [InlineData("An incoming tie could not be matched; review tied notes.", "Ocr_Warning_UnmatchedTie")]
    [InlineData("Only clear, flat piano scans are supported. Review recognized notes, rhythm and ties before playback.", "Ocr_Warning_StaffScanReview")]
    public void ExistingEnglishAddonWarningsUseHostLanguage(string warning, string key)
    {
        var chinese = Resources("zh-CN");
        var english = Resources("en-US");
        Assert.Equal([chinese[key]], OcrWarningLocalizer.Localize([warning], resource => chinese[resource]));
        Assert.NotEqual(warning, chinese[key]);
        Assert.Equal([warning], OcrWarningLocalizer.Localize([warning], resource => english[resource]));
    }

    [Theory]
    [InlineData("未识别速度，初始设置为 120 BPM，请导入后调整。", "Ocr_Warning_TempoDefault")]
    [InlineData("未识别首个拍号，显示暂用 4/4，请核对拍号。", "Ocr_Warning_TimeSignatureDefault")]
    [InlineData("图片末尾有延续到后续页面的连音，请核对结尾时值。", "Ocr_Warning_OpenTies")]
    [InlineData("部分连音起点位于图片之外，请核对连音。", "Ocr_Warning_UnmatchedTie")]
    [InlineData("仅支持清晰平直的钢琴扫描谱，播放前请核对音符、节奏和连音。", "Ocr_Warning_StaffScanReview")]
    public void ExistingChineseAddonWarningsCanBeDisplayedInEnglish(string warning, string key)
    {
        var english = Resources("en-US");
        Assert.Equal([english[key]], OcrWarningLocalizer.Localize([warning], resource => english[resource]));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void DuplicateAliasesAreMergedAfterTranslation_UnknownMessagesArePreserved(string language)
    {
        var resources = Resources(language);
        const string unknown = "Unrecognized model warning: review measure 7.";
        var warnings = OcrWarningLocalizer.Localize([
            "", "  ",
            " Tempo was not recognized; the initial tempo is 120 BPM. Adjust it after importing. ",
            "未识别速度，初始设置为 120 BPM，请导入后调整。", unknown, unknown,
        ], key => resources[key]);
        Assert.Equal([resources["Ocr_Warning_TempoDefault"], unknown], warnings);
    }
}
