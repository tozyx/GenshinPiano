using System.IO;
using System.Text.Json;

namespace GenshinPiano.App.Services;

public sealed record CachedReleaseNotes(string Version, string? ReleaseNotes);

public static class ReleaseNotesCacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string CachePath => Path.Combine(
        AppContext.BaseDirectory,
        "update-cache",
        "latest-release-notes.json");

    public static void Save(string version, string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(
            CachePath,
            JsonSerializer.Serialize(
                new CachedReleaseNotes(version, releaseNotes),
                SerializerOptions));
    }

    public static CachedReleaseNotes? Load()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CachedReleaseNotes>(
                File.ReadAllText(CachePath),
                SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"Could not read cached release notes: {exception.Message}");
            return null;
        }
    }
}
