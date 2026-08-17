using System.Windows;

namespace GenshinPiano.App.Services;

internal static class ResourceDictionarySwitcher
{
    public static void Replace(string sourceMarker, string newSource)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var index = -1;
        for (var candidateIndex = 0; candidateIndex < dictionaries.Count; candidateIndex++)
        {
            if (dictionaries[candidateIndex].Source?.ToString()
                    .Contains(sourceMarker, StringComparison.OrdinalIgnoreCase) == true)
            {
                index = candidateIndex;
                break;
            }
        }

        if (index < 0 || index >= dictionaries.Count)
        {
            throw new InvalidOperationException($"Resource dictionary containing '{sourceMarker}' was not found.");
        }

        dictionaries[index] = new ResourceDictionary
        {
            Source = new Uri(newSource, UriKind.Relative),
        };
    }
}
