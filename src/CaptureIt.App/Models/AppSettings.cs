using System.IO;
namespace CaptureIt.App.Models;

/// <summary>
/// All user-configurable settings, persisted as JSON at
/// %AppData%\CaptureIt\settings.json. Kept as a simple POCO so it round-trips
/// cleanly through System.Text.Json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Folder screenshots are saved to. Falls back to %Pictures%\CaptureIt if invalid at save time.</summary>
    public string SaveFolder { get; set; } = DefaultSaveFolder;

    /// <summary>
    /// Filename pattern (without extension; .png is always appended). Supports the
    /// tokens: {date} (yyyyMMdd), {time} (HHmmss), {datetime} (yyyyMMdd_HHmmss),
    /// {timestamp} (yyyyMMdd_HHmmss - same as {datetime}), {counter} (reserved for
    /// future use). Unknown tokens are left as-is.
    /// </summary>
    public string FilenamePattern { get; set; } = "Screenshot_{datetime}";

    /// <summary>The capture mode the global hotkey should repeat.</summary>
    public CaptureMode LastCaptureMode { get; set; } = CaptureMode.Region;

    /// <summary>
    /// The last region selected in a region capture, in physical-pixel virtual-desktop
    /// coordinates. Pre-shown in the region overlay so Enter re-captures it. Null until
    /// the user has completed at least one region capture.
    /// </summary>
    public CaptureRegion? LastRegion { get; set; }

    /// <summary>
    /// Device name (e.g. \\.\DISPLAY1) of the monitor chosen in the last full-screen
    /// capture. Pre-highlighted in the monitor picker so Enter re-captures it. Null
    /// until the user has completed at least one full-screen capture.
    /// </summary>
    public string? LastMonitorDeviceName { get; set; }

    /// <summary>The user-configurable global hotkey. Defaults to Ctrl+Alt+S.</summary>
    public HotkeyDefinition Hotkey { get; set; } = HotkeyDefinition.Default;

    /// <summary>
    /// Countdown, in seconds, between confirming the region/monitor and taking the
    /// actual screenshot. Lets the user set up a transient UI state (open a menu,
    /// hover a tooltip) before the shot fires. 0 means capture immediately. Only the
    /// values in <see cref="SupportedCaptureDelays"/> are valid; any other value is
    /// normalized back to 0 on load (see <see cref="NormalizeCaptureDelay"/>).
    /// </summary>
    public int CaptureDelaySeconds { get; set; }

    /// <summary>The delay values the UI offers and the only ones treated as valid. 0 = Off.</summary>
    public static readonly IReadOnlyList<int> SupportedCaptureDelays = new[] { 0, 3, 5, 10 };

    /// <summary>
    /// Coerces an arbitrary (possibly hand-edited or corrupted) delay value to a
    /// supported one, falling back to 0 (immediate capture) for anything unsupported.
    /// </summary>
    public static int NormalizeCaptureDelay(int value)
        => SupportedCaptureDelays.Contains(value) ? value : 0;

    public static string DefaultSaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CaptureIt");

    public static string FallbackSaveFolder => DefaultSaveFolder;
}
