namespace GenshinPiano.App.Services;

public static class OcrWarningLocalizer
{
    // Existing protocol-v1 add-ons return display text rather than message identifiers.
    // Keep these aliases so installed add-ons can use the host's current UI language.
    private static string? ResourceKey(string warning) => warning switch
    {
        "Accompaniment voices were detected but excluded from the imported score." => "Ocr_Warning_AccompanimentExcluded",
        "Multiple numbered-notation voices were inferred from the page layout. Review their alignment." => "Ocr_Warning_MultipleVoices",
        "A single numbered-notation voice was inferred from the page layout." => "Ocr_Warning_SingleVoice",
        "Octave dots, rhythm underlines and augmentation dots were inferred geometrically. Review low-confidence symbols before playback." => "Ocr_Warning_Geometry",
        "Recognized accidentals and extension dashes were preserved. Review curved ties and other low-confidence notation marks." => "Ocr_Warning_AccidentalsAndTies",
        "Tempo was not recognized; the initial tempo is 120 BPM. Adjust it after importing." or
            "未识别速度，初始设置为 120 BPM，请导入后调整。" => "Ocr_Warning_TempoDefault",
        "No initial time signature was recognized; 4/4 is used for display. Review the time signature." or
            "未识别首个拍号，显示暂用 4/4，请核对拍号。" => "Ocr_Warning_TimeSignatureDefault",
        "The image ends with open ties; review the ending durations." or
            "图片末尾有延续到后续页面的连音，请核对结尾时值。" => "Ocr_Warning_OpenTies",
        "An incoming tie could not be matched; review tied notes." or
            "部分连音起点位于图片之外，请核对连音。" => "Ocr_Warning_UnmatchedTie",
        "Only clear, flat piano scans are supported. Review recognized notes, rhythm and ties before playback." or
            "仅支持清晰平直的钢琴扫描谱，播放前请核对音符、节奏和连音。" => "Ocr_Warning_StaffScanReview",
        _ => null,
    };

    public static IReadOnlyList<string> Localize(IEnumerable<string> warnings, Func<string, string> getText) =>
        warnings.Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => ResourceKey(warning.Trim()) is { } key ? getText(key) : warning)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
