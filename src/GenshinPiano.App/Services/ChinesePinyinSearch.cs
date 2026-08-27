using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace GenshinPiano.App.Services;

public static class ChinesePinyinSearch
{
    private static readonly CompareInfo ChineseCompareInfo =
        CultureInfo.GetCultureInfo("zh-CN").CompareInfo;

    private static readonly string[] InitialBoundaries =
    [
        "啊", "芭", "擦", "搭", "蛾", "发", "噶", "哈", "击", "喀", "垃", "妈", "拿",
        "哦", "啪", "期", "然", "撒", "塌", "挖", "昔", "压", "匝",
    ];

    private static readonly char[] Initials =
        "ABCDEFGHJKLMNOPQRSTWXYZ".ToCharArray();

    private static readonly ConcurrentDictionary<string, string> SearchKeyCache =
        new(StringComparer.Ordinal);

    public static bool MatchesInitials(string text, string query)
    {
        var normalizedQuery = NormalizeLatinLettersAndDigits(query);
        if (normalizedQuery.Length == 0)
        {
            return false;
        }

        var searchKey = SearchKeyCache.GetOrAdd(text, BuildInitialSearchKey);
        return searchKey.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildInitialSearchKey(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            if (IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            var initial = TryGetChinesePinyinInitial(character);
            if (initial is not null)
            {
                builder.Append(initial.Value);
            }
        }

        return builder.ToString();
    }

    private static string NormalizeLatinLettersAndDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            if (IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static char? TryGetChinesePinyinInitial(char character)
    {
        if (!IsCommonChineseIdeograph(character))
        {
            return null;
        }

        var text = character.ToString();
        for (var index = InitialBoundaries.Length - 1; index >= 0; index--)
        {
            if (ChineseCompareInfo.Compare(
                    text,
                    InitialBoundaries[index],
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth) >= 0)
            {
                return char.ToLowerInvariant(Initials[index]);
            }
        }

        return null;
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsCommonChineseIdeograph(char character) =>
        character is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF';
}
