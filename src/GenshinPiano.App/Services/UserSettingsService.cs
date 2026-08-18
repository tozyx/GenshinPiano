using System.IO;
using System.Text;
using System.Text.Json;

namespace GenshinPiano.App.Services;

public sealed record UserSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public EditorUserSettings Editor { get; init; } = new();
}

public sealed record EditorUserSettings
{
    public int SnapDivision { get; init; } = 4;

    public string DefaultArticulation { get; init; } = "Natural";

    public string PitchLabelMode { get; init; } = "LetterWithKey";

    public bool NaturalSustain { get; init; } = true;
}

public interface IUserSettingsService
{
    UserSettings Current { get; }

    void SetSnapDivision(int value);

    void SetDefaultArticulation(string value);

    void SetPitchLabelMode(string value);

    void SetNaturalSustain(bool value);
}

public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public UserSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "config",
            "settings.json");
        Current = Load();
    }

    public UserSettings Current { get; private set; }

    public void SetSnapDivision(int value)
    {
        if (value is not (1 or 2 or 4 or 8) || Current.Editor.SnapDivision == value)
        {
            return;
        }

        Update(Current.Editor with { SnapDivision = value });
    }

    public void SetDefaultArticulation(string value)
    {
        if (!IsValidArticulation(value) || Current.Editor.DefaultArticulation == value)
        {
            return;
        }

        Update(Current.Editor with { DefaultArticulation = value });
    }

    public void SetPitchLabelMode(string value)
    {
        if (!IsValidPitchLabelMode(value) || Current.Editor.PitchLabelMode == value)
        {
            return;
        }

        Update(Current.Editor with { PitchLabelMode = value });
    }

    public void SetNaturalSustain(bool value)
    {
        if (Current.Editor.NaturalSustain != value)
        {
            Update(Current.Editor with { NaturalSustain = value });
        }
    }

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
            return Normalize(JsonSerializer.Deserialize<UserSettings>(json, SerializerOptions));
        }
        catch (IOException)
        {
            return new UserSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new UserSettings();
        }
        catch (JsonException)
        {
            return new UserSettings();
        }
    }

    private void Update(EditorUserSettings editor)
    {
        Current = Current with { Editor = editor };
        Save();
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _settingsPath + ".tmp";
            var json = JsonSerializer.Serialize(Current, SerializerOptions) + Environment.NewLine;
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (IOException)
        {
            // A settings write failure must not interrupt editing or playback.
        }
        catch (UnauthorizedAccessException)
        {
            // The current session continues with in-memory settings.
        }
    }

    private static UserSettings Normalize(UserSettings? settings)
    {
        var defaults = new EditorUserSettings();
        var editor = settings?.Editor ?? defaults;
        return new UserSettings
        {
            Editor = editor with
            {
                SnapDivision = editor.SnapDivision is 1 or 2 or 4 or 8
                    ? editor.SnapDivision
                    : defaults.SnapDivision,
                DefaultArticulation = IsValidArticulation(editor.DefaultArticulation)
                    ? editor.DefaultArticulation
                    : defaults.DefaultArticulation,
                PitchLabelMode = IsValidPitchLabelMode(editor.PitchLabelMode)
                    ? editor.PitchLabelMode
                    : defaults.PitchLabelMode,
            },
        };
    }

    private static bool IsValidArticulation(string? value) => value is
        "Legato" or "Natural" or "Detached" or "Staccato";

    private static bool IsValidPitchLabelMode(string? value) => value is
        "LetterWithKey" or "NumberedWithKey" or "LetterOnly" or "NumberedOnly";
}
