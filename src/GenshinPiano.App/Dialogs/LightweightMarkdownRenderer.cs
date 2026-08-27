using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GenshinPiano.App.Dialogs;

internal static partial class LightweightMarkdownRenderer
{
    public static FlowDocument Render(
        string markdown, Brush primary, Brush secondary, Brush accent, Brush codeBackground)
    {
        markdown = UnwrapOuterMarkdownFence(markdown);
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = (FontFamily)System.Windows.Application.Current.FindResource("AppFontFamily"),
            FontSize = 13,
            Foreground = secondary,
            LineHeight = 21,
        };
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inCode = false;
        var code = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmedStart = line.TrimStart();
            if (trimmedStart.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode) { AddCodeBlock(document, code, codeBackground); code.Clear(); }
                inCode = !inCode;
                continue;
            }
            if (inCode) { code.Add(rawLine); continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Trim() is "---" or "***" or "___")
            {
                document.Blocks.Add(new BlockUIContainer(new Separator { Margin = new Thickness(0, 8, 0, 8) }));
                continue;
            }
            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                var paragraph = CreateParagraph(heading.Groups[2].Value, primary, accent, codeBackground);
                paragraph.FontSize = level switch { 1 => 22, 2 => 18, _ => 15 };
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.Margin = new Thickness(0, level == 1 ? 8 : 6, 0, 5);
                document.Blocks.Add(paragraph);
                continue;
            }
            if (trimmedStart.StartsWith("> ", StringComparison.Ordinal))
            {
                var paragraph = CreateParagraph(trimmedStart[2..], secondary, accent, codeBackground);
                paragraph.Margin = new Thickness(12, 3, 0, 7);
                paragraph.FontStyle = FontStyles.Italic;
                document.Blocks.Add(paragraph);
                continue;
            }
            var list = ListRegex().Match(line);
            if (list.Success)
            {
                var marker = char.IsDigit(list.Groups[1].Value[0]) ? list.Groups[1].Value : "•";
                var paragraph = CreateParagraph($"{marker} {list.Groups[2].Value}", secondary, accent, codeBackground);
                paragraph.Margin = new Thickness(12, 2, 0, 2);
                document.Blocks.Add(paragraph);
                continue;
            }
            var normal = CreateParagraph(line, secondary, accent, codeBackground);
            normal.Margin = new Thickness(0, 2, 0, 7);
            document.Blocks.Add(normal);
        }
        if (code.Count > 0) AddCodeBlock(document, code, codeBackground);
        return document;
    }

    private static string UnwrapOuterMarkdownFence(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        var last = Array.FindLastIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0 || last <= first)
        {
            return markdown;
        }

        var opening = lines[first].Trim();
        var closing = lines[last].Trim();
        if (!closing.Equals("```", StringComparison.Ordinal) ||
            !MarkdownFenceRegex().IsMatch(opening))
        {
            return markdown;
        }

        return string.Join('\n', lines.Skip(first + 1).Take(last - first - 1));
    }

    private static Paragraph CreateParagraph(string text, Brush foreground, Brush accent, Brush codeBackground)
    {
        var paragraph = new Paragraph { Foreground = foreground };
        var index = 0;
        foreach (Match match in InlineRegex().Matches(text))
        {
            if (match.Index > index) paragraph.Inlines.Add(new Run(text[index..match.Index]));
            if (match.Groups[1].Success)
                paragraph.Inlines.Add(new Bold(new Run(match.Groups[1].Value)));
            else if (match.Groups[2].Success)
                paragraph.Inlines.Add(new Run(match.Groups[2].Value) { FontFamily = new FontFamily("Consolas"), Background = codeBackground });
            else
            {
                var uriText = match.Groups[4].Value;
                if (Uri.TryCreate(uriText, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    var link = new Hyperlink(new Run(match.Groups[3].Value)) { NavigateUri = uri, Foreground = accent };
                    link.RequestNavigate += (_, args) => Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(new Run(match.Value));
            }
            index = match.Index + match.Length;
        }
        if (index < text.Length) paragraph.Inlines.Add(new Run(text[index..]));
        return paragraph;
    }

    private static void AddCodeBlock(FlowDocument document, IEnumerable<string> lines, Brush background)
    {
        document.Blocks.Add(new Paragraph(new Run(string.Join(Environment.NewLine, lines)))
        {
            FontFamily = new FontFamily("Consolas"),
            Background = background,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 5, 0, 8),
        });
    }

    [GeneratedRegex(@"^\s{0,3}(#{1,3})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*((?:[-*+])|(?:\d+\.))\s+(.+)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*|`([^`]+)`|\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex InlineRegex();

    [GeneratedRegex(@"^```(?:markdown|md|text)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownFenceRegex();
}
