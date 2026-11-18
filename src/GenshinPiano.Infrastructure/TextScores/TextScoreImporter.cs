using System.Text.RegularExpressions;
using GenshinPiano.Application.TextScores;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.TextScores;

/// <summary>Author-selected text conventions. Layout inference never chooses a meter.</summary>
public sealed partial class TextScoreImporter : ITextScoreImporter
{
    private sealed record Cell(string Text, int Column);
    private sealed record Row(int Line, IReadOnlyList<Cell> Cells, bool Trailing);
    private sealed record Section(int Index, string Name, List<Row> Rows);
    private sealed record Document(TextScoreNotation Notation, List<Section> Sections,
        List<TextScoreWarning> IgnoredText, int OmittedTextCount);
    private sealed record Attack(int[] Pitches, char Kind = 'n');
    private static bool Grouped(TextScoreProfile profile) => profile is TextScoreProfile.Zhijian or TextScoreProfile.Xingli;

    public TextScoreDetection Detect(string text, TextScoreNotation notation = TextScoreNotation.Auto)
    {
        var candidates = new List<TextScoreProfile>();
        TextScoreNotation detected = TextScoreNotation.Auto;
        TextScoreImportException? firstError = null;
        foreach (var profile in Enum.GetValues<TextScoreProfile>().Where(profile => profile != TextScoreProfile.Unspecified))
        {
            try
            {
                // Match the entire document against each grammar, never just a distinctive prefix.
                var analysis = Analyze(text, profile, notation);
                detected = analysis.Notation;
                candidates.Add(profile);
            }
            catch (TextScoreImportException ex)
            {
                if (ex.Code is "Empty" or "MixedNotation" or "Notation" or "Options") throw;
                firstError ??= ex;
            }
        }
        if (candidates.Count == 0)
        {
            if (firstError?.Code == "Limit") throw firstError;
            throw Error("NoFormat", firstError?.Line ?? 0, firstError?.Column ?? 0, firstError?.Detail ?? "");
        }
        if (candidates.Count == 1) return new(detected, candidates, candidates[0], candidates[0], "UniqueGrammar");

        // Row layout can suggest a convention, but cannot prove whether '/' means a beat or a bar.
        if (candidates.Contains(TextScoreProfile.Zhijian))
        {
            var rows = Read(text, TextScoreProfile.Zhijian, detected).Sections.SelectMany(section => section.Rows).ToArray();
            var fourGroups = rows.Count(row => row.Cells.Count == 4);
            if (rows.Length >= 4 && fourGroups >= rows.Length * 0.7 && rows.Count(row => !row.Trailing) >= rows.Length * 0.5)
                return new(detected, candidates, null, TextScoreProfile.Zhijian, "FourGroupRows");
        }
        return new(detected, candidates, null, null, "SharedSymbols");
    }

    public TextScoreAnalysis Analyze(string text, TextScoreProfile profile, TextScoreNotation notation = TextScoreNotation.Auto)
    {
        var document = Read(text, profile, notation);
        foreach (var row in document.Sections.SelectMany(section => section.Rows))
            foreach (var cell in row.Cells) ReadAttacks(cell, row.Line, profile, document.Notation, false);
        return new(document.Notation, document.Sections.Select(section => new TextScoreSectionInfo(
            section.Index, section.Name, section.Rows[0].Line, section.Rows.Count, SuggestWidth(section, profile))).ToArray());
    }

    public TextScoreImportResult Parse(string text, TextScoreImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(options.Bpm) || options.Bpm is < 1 or > 1000 ||
            string.IsNullOrWhiteSpace(options.Title) || options.Title.Length > 256)
            throw Error("Options");
        var document = Read(text, options.Profile, options.Notation);
        if (options.Sections.Select(section => section.Index).Distinct().Count() != options.Sections.Count ||
            options.Sections.Any(section => section.Index < 0 || section.Index >= document.Sections.Count)) throw Error("Options");
        var notes = new List<NoteEvent>();
        var warnings = new List<TextScoreWarning> { new("EstimatedTiming") };
        var omitted = 0;
        void Warn(string code, int line = 0, string detail = "")
        {
            if (warnings.Count < 200) warnings.Add(new(code, line, detail));
            else omitted++;
        }
        foreach (var warning in document.IgnoredText) Warn(warning.Code, warning.Line, warning.Detail);
        omitted += document.OmittedTextCount;
        long offset = 0;
        var groups = 0;
        var attackCount = 0;
        var signatures = new List<TimeSignatureChange>();
        var previousWidth = 0;
        foreach (var section in document.Sections)
        {
            var setting = options.Sections.SingleOrDefault(item => item.Index == section.Index)
                ?? new TextScoreSectionOptions { Index = section.Index };
            if (!ValidDenominator(setting.GroupDenominator) || !ValidDenominator(setting.Denominator) ||
                setting.Numerator is < 1 or > 32 || setting.GroupsPerRow is < 0 or > 64 ||
                !Enum.IsDefined(setting.TrailingSlash)) throw Error("Options");
            var width = setting.GroupsPerRow == 0 ? SuggestWidth(section, options.Profile) : setting.GroupsPerRow;
            if (Grouped(options.Profile) && previousWidth != 0 && width != previousWidth)
                Warn("LayoutChange", section.Rows[0].Line, width.ToString());
            previousWidth = width;
            var measureTicks = 480L * 4 * setting.Numerator / setting.Denominator;
            if (signatures.Count == 0 || signatures[^1].Numerator != setting.Numerator || signatures[^1].Denominator != setting.Denominator)
            {
                if (signatures.Count > 0)
                {
                    var previous = signatures[^1];
                    var previousMeasure = 480L * 4 * previous.Numerator / previous.Denominator;
                    if ((offset - previous.Tick) % previousMeasure != 0) Warn("MeterBoundary", section.Rows[0].Line);
                }
                signatures.Add(new() { Tick = offset, Numerator = setting.Numerator, Denominator = setting.Denominator });
            }
            foreach (var row in section.Rows)
            {
                var count = row.Cells.Count;
                if (row.Trailing)
                {
                    var closing = setting.TrailingSlash switch
                    {
                        TextScoreTrailingSlash.Closing => true,
                        TextScoreTrailingSlash.Separator => false,
                        _ when options.Profile != TextScoreProfile.Zhijian => true,
                        _ => count != width,
                    };
                    if (setting.TrailingSlash == TextScoreTrailingSlash.Auto && options.Profile == TextScoreProfile.Zhijian &&
                        count != width && count != width + 1) Warn("TrailingAmbiguous", row.Line);
                    if (closing) count--;
                }
                if (Grouped(options.Profile) && count != width) Warn("RowWidth", row.Line, $"{count}/{width}");
                var cellTicks = Grouped(options.Profile) ? 480L * 4 / setting.GroupDenominator : measureTicks;
                for (var cellIndex = 0; cellIndex < count; cellIndex++)
                {
                    if (++groups > TextScoreImportLimits.MaxGroups) throw Error("Limit", row.Line);
                    var attacks = ReadAttacks(row.Cells[cellIndex], row.Line, options.Profile, document.Notation, options.SpacesAreRests);
                    if (attacks.Count > cellTicks) throw Error("Precision", row.Line, row.Cells[cellIndex].Column);
                    for (var i = 0; i < attacks.Count; i++)
                    {
                        var attack = attacks[i];
                        var start = offset + Boundary(i, cellTicks, attacks.Count);
                        var end = offset + Boundary(i + 1, cellTicks, attacks.Count);
                        if (attack.Pitches.Length == 0) continue;
                        if (attack.Kind != 'n') Warn("Ornament", row.Line);
                        if (attack.Kind == 'r')
                        {
                            for (var j = 0; j < attack.Pitches.Length; j++)
                            {
                                var subStart = start + Boundary(j, end - start, attack.Pitches.Length);
                                var subEnd = start + Boundary(j + 1, end - start, attack.Pitches.Length);
                                Add(attack.Pitches[j], subStart, subEnd - subStart, row.Line);
                                attackCount++;
                            }
                        }
                        else if (attack.Kind == 'a')
                        {
                            // Stagger the attack across the first fifth, sharing the event's end.
                            var spread = (end - start) / 5;
                            if (attack.Pitches.Length > 1 && spread < attack.Pitches.Length) throw Error("Precision", row.Line);
                            for (var j = 0; j < attack.Pitches.Length; j++)
                            {
                                var subStart = start + Boundary(j, spread, attack.Pitches.Length);
                                Add(attack.Pitches[j], subStart, end - subStart, row.Line);
                                attackCount++;
                            }
                        }
                        else
                        {
                            foreach (var pitch in attack.Pitches) Add(pitch, start, end - start, row.Line);
                            attackCount++;
                        }
                    }
                    offset = checked(offset + cellTicks);
                }
            }
        }
        if (notes.Count == 0) throw Error("Empty");
        if (offset > notes.Max(note => note.StartTick + note.RhythmTick!.Value)) Warn("TrailingRest");
        if (omitted > 0) warnings.Add(new("More", Detail: omitted.ToString()));
        var score = NoteDurationCalculator.ApplyAutoDurations(new()
        {
            Metadata = new() { Title = options.Title.Trim() },
            Timing = new() { Ppq = 480, TempoMap = [new() { Bpm = options.Bpm }], TimeSignatures = signatures },
            Tracks = [new() { Id = "text-main", Name = "Text", Instrument = "windsong-lyre", Notes = notes }],
            Playback = new() { Mapping = "genshin-21-key", OutOfRangePolicy = OutOfRangePolicy.Reject },
        });
        if (ScoreValidator.Validate(score).Count != 0) throw Error("Options");
        return new(score, attackCount, offset, warnings);

        void Add(int pitch, long start, long span, int line)
        {
            if (span <= 0) throw Error("Precision", line);
            if (notes.Count >= TextScoreImportLimits.MaxNotes) throw Error("Limit", line);
            notes.Add(new() { Pitch = pitch, StartTick = start, RhythmTick = span, DurationTick = span,
                DurationMode = DurationMode.Auto, GateRatio = 0.8 });
        }
    }

    private static long Boundary(int index, long length, int count) =>
        (long)Math.Round(index * (double)length / count, MidpointRounding.AwayFromZero);
    private static bool ValidDenominator(int value) => value is 1 or 2 or 4 or 8 or 16 or 32;
    private static TextScoreImportException Error(string code, int line = 0, int column = 0, string detail = "") =>
        new(code, line, column, detail);

    private static Document Read(string text, TextScoreProfile profile, TextScoreNotation notation)
    {
        if (!Enum.IsDefined(profile) || profile == TextScoreProfile.Unspecified) throw Error("Profile");
        if (!Enum.IsDefined(notation)) throw Error("Options");
        if (string.IsNullOrWhiteSpace(text)) throw Error("Empty");
        if (text.Length > TextScoreImportLimits.MaxTextLength) throw Error("Limit");
        var sections = new List<Section>();
        var ignoredText = new List<TextScoreWarning>();
        var omittedTextCount = 0;
        var current = new Section(0, "", []);
        var rows = 0;
        var hasKeys = false;
        var hasNumbers = false;
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // Copied web text may contain encoded spaces and Markdown hard-line-break markers.
            // Pad entities to their original length so error columns still refer to the source.
            var source = SpaceEntity().Replace(lines[i], match => new string(' ', match.Length));
            var end = source.Length;
            while (end > 0 && (char.IsWhiteSpace(source[end - 1]) || source[end - 1] == '\\' || source[end - 1] == '＼')) end--;
            source = source[..end];
            var line = Normalize(source);
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (Heading().IsMatch(line.Trim()))
            {
                var name = source.Trim();
                if (name.Length > 256) name = name[..255] + "…";
                if (current.Rows.Count > 0)
                {
                    sections.Add(current);
                    if (sections.Count >= TextScoreImportLimits.MaxSections) throw Error("Limit", i + 1);
                    current = new(sections.Count, name, []);
                }
                else
                {
                    // Consecutive labels such as "主歌：(DAOKO)" then "A段" describe one section.
                    name = string.IsNullOrEmpty(current.Name) ? name : current.Name + " · " + name;
                    current = current with { Name = name.Length <= 256 ? name : name[..255] + "…" };
                }
                continue;
            }
            if (IsExplanation(line.Trim()))
            {
                if (ignoredText.Count < 199)
                {
                    var detail = source.Trim();
                    ignoredText.Add(new("IgnoredText", i + 1, detail.Length <= 120 ? detail : detail[..119] + "…"));
                }
                else omittedTextCount++;
                continue;
            }
            foreach (var c in line)
            {
                if (c is >= 'A' and <= 'Z') hasKeys = true;
                if (c is >= '1' and <= '7') hasNumbers = true;
            }
            var start = 0;
            for (var j = 0; j <= line.Length; j++)
            {
                if (j < line.Length && !(Grouped(profile) && line[j] is '\\' or '|')) continue;
                if (j > start && !string.IsNullOrWhiteSpace(line[start..j]))
                {
                    if (++rows > TextScoreImportLimits.MaxRows) throw Error("Limit", i + 1);
                    var cells = new List<Cell>();
                    var cellStart = start;
                    for (var k = start; k <= j; k++)
                    {
                        if (k < j && line[k] != (profile == TextScoreProfile.Yujun ? '|' : '/')) continue;
                        cells.Add(new(line[cellStart..k], cellStart + 1));
                        cellStart = k + 1;
                    }
                    var trailing = line[start..j].TrimEnd().EndsWith(profile == TextScoreProfile.Yujun ? '|' : '/');
                    current.Rows.Add(new(i + 1, cells, trailing));
                }
                start = j + 1;
            }
        }
        if (current.Rows.Count > 0) sections.Add(current);
        if (sections.Count == 0) throw Error("Empty");
        if (hasKeys && hasNumbers) throw Error("MixedNotation");
        var detected = hasNumbers ? TextScoreNotation.Numbers : TextScoreNotation.Keys;
        if (notation != TextScoreNotation.Auto && notation != detected) throw Error("Notation");
        return new(detected, sections, ignoredText, omittedTextCount);
    }

    private static bool IsExplanation(string line) => ExplanationPrefix().IsMatch(line) ||
        // Plain Chinese prose is safe to treat as a note only on a separate line. Musical rows
        // and unlabelled text containing score separators still go through strict validation.
        (line[0] is >= '\u4e00' and <= '\u9fff' && line.IndexOfAny(['/', '|', '\\']) < 0);

    private static int SuggestWidth(Section section, TextScoreProfile profile)
    {
        if (!Grouped(profile)) return 1;
        return section.Rows.Select(row => Math.Max(1, row.Cells.Count -
                (row.Trailing && (profile == TextScoreProfile.Xingli || row.Cells.Count != 4) ? 1 : 0)))
            .GroupBy(width => width).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
    }

    private static List<Attack> ReadAttacks(Cell cell, int line, TextScoreProfile profile,
        TextScoreNotation notation, bool spacesAreRests)
    {
        var result = new List<Attack>();
        var text = cell.Text;
        var index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
                if (spacesAreRests && result.Count > 0 && index < text.Length) result.Add(new([]));
                continue;
            }
            var c = text[index];
            var kind = c == '(' ? 'n' : c == '[' && profile is TextScoreProfile.Xingli or TextScoreProfile.Touming ? 'r' :
                c == '{' && profile == TextScoreProfile.Yujun || c == '〖' && profile == TextScoreProfile.Touming ? 'a' : '\0';
            if (kind != '\0')
            {
                var close = c switch { '(' => ')', '[' => ']', '{' => '}', _ => '〗' };
                index++;
                var pitches = new List<int>();
                while (index < text.Length && text[index] != close)
                {
                    if (char.IsWhiteSpace(text[index])) { index++; continue; }
                    pitches.Add(ReadPitch());
                }
                if (index == text.Length || pitches.Count == 0) throw Error("Bracket", line, cell.Column + index);
                if (kind == 'n' && pitches.Distinct().Count() != pitches.Count) throw Error("Duplicate", line, cell.Column + index);
                index++;
                result.Add(new(pitches.ToArray(), kind));
            }
            else result.Add(new([ReadPitch()]));
        }
        return result;

        int ReadPitch()
        {
            if (index >= text.Length) throw Error("Symbol", line, cell.Column + index);
            var column = cell.Column + index;
            var symbol = text[index++];
            char key;
            if (notation == TextScoreNotation.Numbers)
            {
                var row = symbol == '+' ? "QWERTYU" : symbol == '-' ? "ZXCVBNM" : "ASDFGHJ";
                if (symbol is '+' or '-')
                {
                    if (index >= text.Length) throw Error("Symbol", line, column, symbol.ToString());
                    symbol = text[index++];
                }
                if (symbol is < '1' or > '7') throw Error("Symbol", line, column, symbol.ToString());
                key = row[symbol - '1'];
            }
            else key = symbol;
            if (!"ZXCVBNMASDFGHJQWERTYU".Contains(key) || !Enum.TryParse<GenshinKey>(key.ToString(), out var genshinKey) ||
                !GenshinKeyMap.TryGetPitch(genshinKey, out var pitch)) throw Error("Symbol", line, column, symbol.ToString());
            return pitch;
        }
    }

    private static string Normalize(string line) => string.Concat(line.Select(c => c switch
    {
        '（' => '(', '）' => ')', '［' => '[', '］' => ']', '｛' => '{', '｝' => '}',
        '／' => '/', '＼' => '\\', '｜' => '|', '＋' => '+', '－' or '−' => '-',
        >= '０' and <= '９' => (char)('0' + c - '０'),
        >= 'a' and <= 'z' => char.ToUpperInvariant(c), _ => c,
    }));

    [GeneratedRegex(@"^(第[一二三四五六七八九十百零上下末\d]+[段页章]|[A-Z][段部]|前奏|主歌|副歌|预副歌|导歌|间奏|桥段|过门|尾奏|尾声|结尾|引子|Section\s+\d+|Intro|Verse(?:\s+\d+)?|(?:Pre[- ]?)?Chorus(?:\s+\d+)?|Bridge|Interlude|Outro|Ending)(?:\s*[:：]\s*[^/|\\]*|\s*\([^/|\\]*\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?:#|(?:说明|备注|提示|注意|注|曲名|标题|作者|编配|制谱|谱师|原曲|演唱|速度|调号|拍号|BPM|Tempo|Title|Author|Note|Notes|Tip|Comment)\s*[:：])", RegexOptions.IgnoreCase)]
    private static partial Regex ExplanationPrefix();

    [GeneratedRegex(@"&(?:\#x0*20|\#0*32|\#x0*a0|\#0*160|nbsp);", RegexOptions.IgnoreCase)]
    private static partial Regex SpaceEntity();
}
