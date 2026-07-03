using System.IO;
using System.Text.Json;
using CaptureIt.App.Models;

namespace CaptureIt.App.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON at %AppData%\CaptureIt\settings.json.
/// Recovers to defaults (rather than crashing the app) if the file is missing,
/// unreadable, or corrupted — a tray-only background app must always be able to start.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsFilePath { get; }

    public SettingsService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CaptureIt");
        SettingsFilePath = Path.Combine(appDataFolder, "settings.json");
    }

    /// <summary>
    /// Test seam: points the settings file at a caller-supplied folder instead of
    /// %AppData%. Not intended for production use (hence internal).
    /// </summary>
    internal SettingsService(string overrideFolder)
    {
        SettingsFilePath = Path.Combine(overrideFolder, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                return RecoverWithDefaults();
            }

            // Guard against a corrupted/edited file producing an invalid combination.
            if (string.IsNullOrWhiteSpace(settings.SaveFolder))
            {
                settings.SaveFolder = AppSettings.DefaultSaveFolder;
            }
            if (string.IsNullOrWhiteSpace(settings.FilenamePattern))
            {
                settings.FilenamePattern = new AppSettings().FilenamePattern;
            }
            settings.Hotkey ??= HotkeyDefinition.Default;

            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return RecoverWithDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);

        // Write to a temp file then replace, so a crash mid-write can't corrupt the
        // settings file that's read on next startup.
        var tempPath = SettingsFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsFilePath, overwrite: true);
    }

    private AppSettings RecoverWithDefaults()
    {
        var defaults = new AppSettings();
        try
        {
            Save(defaults);
        }
        catch (Exception)
        {
            // If we can't even write defaults, still return them in-memory so the
            // app can run for this session.
        }
        return defaults;
    }
}
