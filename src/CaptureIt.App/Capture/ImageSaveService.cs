using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using CaptureIt.App.Models;

namespace CaptureIt.App.Capture;

public sealed class SaveResult
{
    public required string SavedFilePath { get; init; }
    public bool UsedFallbackFolder { get; init; }
    public string? FallbackReason { get; init; }
}

/// <summary>
/// Saves captured bitmaps to disk as PNG using the configured filename pattern, with
/// filename sanitization, same-second collision suffixing, and an automatic fallback
/// to %Pictures%\CaptureIt if the configured save folder is unusable (deleted,
/// unplugged drive, permission denied, etc.).
/// </summary>
public static class ImageSaveService
{
    // Windows' invalid filename characters, hardcoded rather than sourced from
    // Path.GetInvalidFileNameChars() — that API returns an OS-dependent set, and
    // this app always targets Windows regardless of which OS it happens to be
    // built/tested on.
    private static readonly char[] InvalidFileNameChars =
        { '<', '>', ':', '"', '/', '\\', '|', '?', '*',
          '\u0000','\u0001','\u0002','\u0003','\u0004','\u0005','\u0006','\u0007',
          '\u0008','\u0009','\u000A','\u000B','\u000C','\u000D','\u000E','\u000F',
          '\u0010','\u0011','\u0012','\u0013','\u0014','\u0015','\u0016','\u0017',
          '\u0018','\u0019','\u001A','\u001B','\u001C','\u001D','\u001E','\u001F' };

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Attempts to save to the configured folder; falls back to
    /// <see cref="AppSettings.FallbackSaveFolder"/> if that folder is not writable.
    /// Throws only if even the fallback folder cannot be used.
    /// </summary>
    public static SaveResult Save(Bitmap bitmap, AppSettings settings)
    {
        var fileNameWithoutExt = BuildFileName(settings.FilenamePattern, DateTime.Now);

        var (folder, usedFallback, fallbackReason) = ResolveWritableFolder(settings.SaveFolder);
        var path = ResolveCollisionFreeFilePath(folder, fileNameWithoutExt, ".png");

        bitmap.Save(path, ImageFormat.Png);

        return new SaveResult
        {
            SavedFilePath = path,
            UsedFallbackFolder = usedFallback,
            FallbackReason = fallbackReason
        };
    }

    /// <summary>
    /// Builds a sanitized filename (without extension) from the configured pattern,
    /// substituting supported tokens.
    /// </summary>
    public static string BuildFileName(string pattern, DateTime timestamp)
    {
        var expanded = pattern
            .Replace("{datetime}", timestamp.ToString("yyyyMMdd_HHmmss"))
            .Replace("{timestamp}", timestamp.ToString("yyyyMMdd_HHmmss"))
            .Replace("{date}", timestamp.ToString("yyyyMMdd"))
            .Replace("{time}", timestamp.ToString("HHmmss"));

        return Sanitize(expanded);
    }

    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Screenshot";
        }

        var sb = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            sb.Append(Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c);
        }

        var result = sb.ToString().Trim().TrimEnd('.');
        if (result.Length == 0)
        {
            result = "Screenshot";
        }

        // Windows reserved device names are invalid even with an extension appended.
        var baseName = result.Split('.')[0];
        if (ReservedWindowsNames.Contains(baseName))
        {
            result = "_" + result;
        }

        // Defend against absurdly long patterns blowing the max path.
        const int maxNameLength = 150;
        if (result.Length > maxNameLength)
        {
            result = result[..maxNameLength];
        }

        return result;
    }

    /// <summary>
    /// If <paramref name="targetFile"/> already exists (e.g. two captures within the
    /// same second), appends _001, _002, ... until a free name is found.
    /// </summary>
    private static string ResolveCollisionFreeFilePath(string folder, string fileNameWithoutExt, string extension)
    {
        var candidate = Path.Combine(folder, fileNameWithoutExt + extension);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(folder, $"{fileNameWithoutExt}_{i:000}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Extremely unlikely fallback: use a GUID suffix to guarantee uniqueness.
        return Path.Combine(folder, $"{fileNameWithoutExt}_{Guid.NewGuid():N}{extension}");
    }

    /// <summary>
    /// Ensures the configured folder exists and is writable; if not, falls back to
    /// %Pictures%\CaptureIt (creating it if necessary) and reports why the fallback
    /// was used so the caller can notify the user.
    /// </summary>
    private static (string Folder, bool UsedFallback, string? Reason) ResolveWritableFolder(string configuredFolder)
    {
        if (TryEnsureWritableFolder(configuredFolder, out var reason))
        {
            return (configuredFolder, false, null);
        }

        var fallback = AppSettings.FallbackSaveFolder;
        if (TryEnsureWritableFolder(fallback, out var fallbackReason))
        {
            return (fallback, true, reason);
        }

        throw new IOException(
            $"Could not save screenshot: configured folder failed ({reason}) and fallback folder failed ({fallbackReason}).");
    }

    private static bool TryEnsureWritableFolder(string folder, out string? failureReason)
    {
        try
        {
            Directory.CreateDirectory(folder);

            // Probe writability explicitly; CreateDirectory succeeding doesn't guarantee
            // we can create files there (e.g. read-only network share).
            var probePath = Path.Combine(folder, $".captureit_write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probePath, Array.Empty<byte>());
            File.Delete(probePath);

            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
        {
            failureReason = ex.Message;
            return false;
        }
    }
}
