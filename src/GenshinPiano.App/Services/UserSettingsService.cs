using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using GenshinPiano.Application.Playback;
using Microsoft.Win32;

namespace GenshinPiano.App.Services;

public sealed record UserSettings
{
    public const int CurrentVersion = 9;

    public int Version { get; init; } = CurrentVersion;

    public EditorUserSettings Editor { get; init; } = new();

    public AppearanceUserSettings Appearance { get; init; } = new();

    public LibraryUserSettings Library { get; init; } = new();

    public UpdateUserSettings Update { get; init; } = new();

    public NotificationUserSettings Notifications { get; init; } = new();

    public OcrUserSettings Ocr { get; init; } = new();
}

public sealed record OcrUserSettings
{
    public string NotationHint { get; init; } = "Auto";
    public string WatermarkMode { get; init; } = "Auto";
    public bool IncludeAccompaniment { get; init; } = true;
    public bool AutoMapTo21Keys { get; init; }
    public bool PreferGpuAcceleration { get; init; } = true;
}

public sealed record NotificationUserSettings
{
    public bool NotifyWhenOcrCompletes { get; init; } = true;
}

public sealed record UpdateUserSettings
{
    public bool NetworkAccessEnabled { get; init; } = true;

    public bool AutomaticUpdatesEnabled { get; init; } = true;

    public string Channel { get; init; } = "preview";
}

public sealed record LibraryUserSettings
{
    public string ScoreFolder { get; init; } = string.Empty;
}

public sealed record AppearanceUserSettings
{
    public string Theme { get; init; } = nameof(AppTheme.Dark);

    public string Language { get; init; } = nameof(AppLanguage.SimplifiedChinese);
}

public sealed record EditorUserSettings
{
    public int SnapDivision { get; init; } = 4;

    public double NewNoteLengthFactor { get; init; } = 0.25;

    public string DefaultArticulation { get; init; } = "Natural";

    public string PitchLabelMode { get; init; } = "LetterWithKey";

    public bool NaturalSustain { get; init; } = true;

    public int AuditionInstrument { get; init; }

    public int AuditionVolume { get; init; } = 80;

    public string PianoRollFrameRate { get; init; } = "VSync";

    public string PianoRollLayoutMode { get; init; } = "Genshin21";

    public double PianoRollPixelsPerBeat { get; init; } = 112;

    public double PianoRollRowHeight { get; init; } = 24;
}

public interface IUserSettingsService
{
    UserSettings Current { get; }

    void SetSnapDivision(int value);

    void SetNewNoteLengthFactor(double value);

    void SetDefaultArticulation(string value);

    void SetPitchLabelMode(string value);

    void SetNaturalSustain(bool value);

    void SetTheme(AppTheme value);

    void SetLanguage(AppLanguage value);

    void SetAuditionInstrument(int value);

    void SetAuditionVolume(int value);

    void SetPianoRollFrameRate(string value);

    void SetPianoRollLayoutMode(string value);

    void SetPianoRollPixelsPerBeat(double value);

    void SetPianoRollRowHeight(double value);

    void SetScoreFolder(string? path);

    void SetNetworkAccessEnabled(bool value);

    void SetAutomaticUpdatesEnabled(bool value);

    void SetUpdateChannel(string value);

    void SetNotifyWhenOcrCompletes(bool value);

    void SetOcrOptions(string notationHint, string watermarkMode, bool includeAccompaniment, bool autoMapTo21Keys, bool preferGpuAcceleration);
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
        var isFirstRun = !File.Exists(_settingsPath);
        Current = Load();
        if (isFirstRun)
        {
            Save();
        }
    }

    public UserSettings Current { get; private set; }

    public void SetSnapDivision(int value)
    {
        if (value is not (1 or 2 or 4 or 8) || Current.Editor.SnapDivision == value)
        {
            return;
        }

        Update(Current with { Editor = Current.Editor with { SnapDivision = value } });
    }

    public void SetNewNoteLengthFactor(double value)
    {
        if (!double.IsFinite(value) || value is <= 0 or > 64 ||
            Math.Abs(Current.Editor.NewNoteLengthFactor - value) < 0.000001)
        {
            return;
        }

        Update(Current with
        {
            Editor = Current.Editor with { NewNoteLengthFactor = value },
        });
    }

    public void SetDefaultArticulation(string value)
    {
        if (!IsValidArticulation(value) || Current.Editor.DefaultArticulation == value)
        {
            return;
        }

        Update(Current with { Editor = Current.Editor with { DefaultArticulation = value } });
    }

    public void SetPitchLabelMode(string value)
    {
        if (!IsValidPitchLabelMode(value) || Current.Editor.PitchLabelMode == value)
        {
            return;
        }

        Update(Current with { Editor = Current.Editor with { PitchLabelMode = value } });
    }

    public void SetNaturalSustain(bool value)
    {
        if (Current.Editor.NaturalSustain != value)
        {
            Update(Current with { Editor = Current.Editor with { NaturalSustain = value } });
        }
    }

    public void SetTheme(AppTheme value)
    {
        var name = value.ToString();
        if (Current.Appearance.Theme != name)
        {
            Update(Current with { Appearance = Current.Appearance with { Theme = name } });
        }
    }

    public void SetLanguage(AppLanguage value)
    {
        var name = value.ToString();
        if (Current.Appearance.Language != name)
        {
            Update(Current with { Appearance = Current.Appearance with { Language = name } });
        }
    }

    public void SetAuditionInstrument(int value)
    {
        value = Math.Clamp(value, AuditionInstrumentIds.LeapingSpiritPiano, 127);
        if (Current.Editor.AuditionInstrument != value)
        {
            Update(Current with { Editor = Current.Editor with { AuditionInstrument = value } });
        }
    }

    public void SetAuditionVolume(int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (Current.Editor.AuditionVolume != value)
        {
            Update(Current with { Editor = Current.Editor with { AuditionVolume = value } });
        }
    }

    public void SetPianoRollFrameRate(string value)
    {
        if (!IsValidPianoRollFrameRate(value) || Current.Editor.PianoRollFrameRate == value)
        {
            return;
        }

        Update(Current with { Editor = Current.Editor with { PianoRollFrameRate = value } });
    }

    public void SetPianoRollLayoutMode(string value)
    {
        if (!IsValidPianoRollLayoutMode(value) || Current.Editor.PianoRollLayoutMode == value)
        {
            return;
        }

        Update(Current with { Editor = Current.Editor with { PianoRollLayoutMode = value } });
    }

    public void SetPianoRollPixelsPerBeat(double value)
    {
        value = Math.Clamp(value, 48, 320);
        if (Math.Abs(Current.Editor.PianoRollPixelsPerBeat - value) >= 0.01)
        {
            Update(Current with { Editor = Current.Editor with { PianoRollPixelsPerBeat = value } });
        }
    }

    public void SetPianoRollRowHeight(double value)
    {
        value = Math.Clamp(value, 18, 42);
        if (Math.Abs(Current.Editor.PianoRollRowHeight - value) >= 0.01)
        {
            Update(Current with { Editor = Current.Editor with { PianoRollRowHeight = value } });
        }
    }

    public void SetScoreFolder(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        if (Current.Library.ScoreFolder != normalized)
        {
            Update(Current with { Library = Current.Library with { ScoreFolder = normalized } });
        }
    }

    public void SetNetworkAccessEnabled(bool value)
    {
        if (Current.Update.NetworkAccessEnabled != value)
        {
            Update(Current with
            {
                Update = Current.Update with { NetworkAccessEnabled = value },
            });
        }
    }

    public void SetAutomaticUpdatesEnabled(bool value)
    {
        if (Current.Update.AutomaticUpdatesEnabled != value)
        {
            Update(Current with
            {
                Update = Current.Update with { AutomaticUpdatesEnabled = value },
            });
        }
    }

    public void SetUpdateChannel(string value)
    {
        if (value is not ("stable" or "preview") || Current.Update.Channel == value) return;
        Update(Current with { Update = Current.Update with { Channel = value } });
    }

    public void SetNotifyWhenOcrCompletes(bool value)
    {
        if (Current.Notifications.NotifyWhenOcrCompletes != value)
        {
            Update(Current with
            {
                Notifications = Current.Notifications with { NotifyWhenOcrCompletes = value },
            });
        }
    }

    public void SetOcrOptions(string notationHint, string watermarkMode, bool includeAccompaniment, bool autoMapTo21Keys, bool preferGpuAcceleration)
    {
        if (notationHint is not ("Auto" or "Numbered" or "Staff") ||
            watermarkMode is not ("Auto" or "Strong" or "Off")) return;
        var value = new OcrUserSettings
        {
            NotationHint = notationHint,
            WatermarkMode = watermarkMode,
            IncludeAccompaniment = includeAccompaniment,
            AutoMapTo21Keys = autoMapTo21Keys,
            PreferGpuAcceleration = preferGpuAcceleration,
        };
        if (Current.Ocr != value) Update(Current with { Ocr = value });
    }

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return CreateFirstRunSettings();
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

    private void Update(UserSettings settings)
    {
        Current = settings;
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
        var appearanceDefaults = new AppearanceUserSettings();
        var appearance = settings?.Appearance ?? appearanceDefaults;
        var library = settings?.Library ?? new LibraryUserSettings();
        var updateDefaults = new UpdateUserSettings();
        var update = settings?.Update ?? updateDefaults;
        var ocrDefaults = new OcrUserSettings();
        var ocr = settings?.Ocr ?? ocrDefaults;
        return new UserSettings
        {
            Version = UserSettings.CurrentVersion,
            Editor = editor with
            {
                SnapDivision = editor.SnapDivision is 1 or 2 or 4 or 8
                    ? editor.SnapDivision
                    : defaults.SnapDivision,
                NewNoteLengthFactor = double.IsFinite(editor.NewNoteLengthFactor) &&
                                      editor.NewNoteLengthFactor is > 0 and <= 64
                    ? editor.NewNoteLengthFactor
                    : defaults.NewNoteLengthFactor,
                DefaultArticulation = IsValidArticulation(editor.DefaultArticulation)
                    ? editor.DefaultArticulation
                    : defaults.DefaultArticulation,
                PitchLabelMode = IsValidPitchLabelMode(editor.PitchLabelMode)
                    ? editor.PitchLabelMode
                    : defaults.PitchLabelMode,
                AuditionInstrument = Math.Clamp(editor.AuditionInstrument, AuditionInstrumentIds.LeapingSpiritPiano, 127),
                AuditionVolume = Math.Clamp(editor.AuditionVolume, 0, 100),
                PianoRollFrameRate = IsValidPianoRollFrameRate(editor.PianoRollFrameRate)
                    ? editor.PianoRollFrameRate
                    : defaults.PianoRollFrameRate,
                PianoRollLayoutMode = IsValidPianoRollLayoutMode(editor.PianoRollLayoutMode)
                    ? editor.PianoRollLayoutMode
                    : defaults.PianoRollLayoutMode,
                PianoRollPixelsPerBeat = double.IsFinite(editor.PianoRollPixelsPerBeat)
                    ? Math.Clamp(editor.PianoRollPixelsPerBeat, 48, 320)
                    : defaults.PianoRollPixelsPerBeat,
                PianoRollRowHeight = double.IsFinite(editor.PianoRollRowHeight)
                    ? Math.Clamp(editor.PianoRollRowHeight, 18, 42)
                    : defaults.PianoRollRowHeight,
            },
            Appearance = appearance with
            {
                Theme = Enum.TryParse<AppTheme>(appearance.Theme, out var theme)
                    ? theme.ToString()
                    : appearanceDefaults.Theme,
                Language = Enum.TryParse<AppLanguage>(appearance.Language, out var language)
                    ? language.ToString()
                    : appearanceDefaults.Language,
            },
            Library = library with
            {
                ScoreFolder = Directory.Exists(library.ScoreFolder) ? library.ScoreFolder : string.Empty,
            },
            Update = update with
            {
                Channel = update.Channel is "stable" or "preview"
                    ? update.Channel
                    : updateDefaults.Channel,
            },
            Notifications = settings?.Notifications ?? new NotificationUserSettings(),
            Ocr = ocr with
            {
                NotationHint = ocr.NotationHint is "Auto" or "Numbered" or "Staff"
                    ? ocr.NotationHint
                    : ocrDefaults.NotationHint,
                WatermarkMode = ocr.WatermarkMode is "Auto" or "Strong" or "Off"
                    ? ocr.WatermarkMode
                    : ocrDefaults.WatermarkMode,
            },
        };
    }

    private static UserSettings CreateFirstRunSettings()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.SimplifiedChinese
            : AppLanguage.English;
        var bundledSongsDirectory = Path.Combine(AppContext.BaseDirectory, "songs");

        return new UserSettings
        {
            Appearance = new AppearanceUserSettings
            {
                Theme = GetWindowsAppTheme().ToString(),
                Language = language.ToString(),
            },
            Library = new LibraryUserSettings
            {
                ScoreFolder = Directory.Exists(bundledSongsDirectory)
                    ? bundledSongsDirectory
                    : string.Empty,
            },
        };
    }

    private static AppTheme GetWindowsAppTheme()
    {
        if (!OperatingSystem.IsWindows())
        {
            return AppTheme.Dark;
        }

        try
        {
            const string personalizeKey =
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            var value = Registry.GetValue(personalizeKey, "AppsUseLightTheme", 0);
            return value is int and not 0 ? AppTheme.Light : AppTheme.Dark;
        }
        catch (IOException)
        {
            return AppTheme.Dark;
        }
        catch (UnauthorizedAccessException)
        {
            return AppTheme.Dark;
        }
        catch (SecurityException)
        {
            return AppTheme.Dark;
        }
    }

    private static bool IsValidArticulation(string? value) => value is
        "Legato" or "Natural" or "Detached" or "Staccato";

    private static bool IsValidPitchLabelMode(string? value) => value is
        "LetterWithKey" or "NumberedWithKey" or "LetterOnly" or "NumberedOnly";

    private static bool IsValidPianoRollFrameRate(string? value) =>
        value is "30" or "60" or "VSync";

    private static bool IsValidPianoRollLayoutMode(string? value) =>
        value is "Genshin21" or "Piano88" or "UsedRange";
}
