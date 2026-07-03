namespace CaptureIt.App.Models;

/// <summary>
/// Describes a single physical monitor in the virtual desktop's coordinate space
/// (physical pixels, which may include negative coordinates for monitors positioned
/// left of or above the primary monitor).
/// </summary>
public sealed class MonitorInfo
{
    /// <summary>Stable 1-based index used for the monitor-picker overlay (keyboard shortcuts, labels).</summary>
    public int Index { get; init; }

    /// <summary>Bounds of this monitor in physical-pixel virtual-desktop coordinates.</summary>
    public required System.Drawing.Rectangle Bounds { get; init; }

    /// <summary>Bounds of the monitor's work area (excludes taskbar), in physical pixels.</summary>
    public System.Drawing.Rectangle WorkingArea { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>Raw device name (e.g. \\.\DISPLAY1), useful for diagnostics.</summary>
    public string DeviceName { get; init; } = string.Empty;
}
