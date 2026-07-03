using CaptureIt.App.Capture;
using CaptureIt.App.Models;
using CaptureIt.App.Overlays;
using CaptureIt.App.Settings;
using CaptureIt.App.TrayIcon;

namespace CaptureIt.App.Core;

/// <summary>
/// Orchestrates a full capture: freeze the desktop, show the appropriate overlay,
/// crop, save, and persist which mode was used (for the hotkey to repeat). Guards
/// against re-entrant capture requests (e.g. hotkey spam or tray-menu double
/// clicks) while a capture/overlay is already in progress.
/// </summary>
public sealed class CaptureController
{
    private readonly SettingsService _settingsService;
    private readonly TrayIconManager _trayIconManager;
    private volatile bool _captureInProgress;

    public CaptureController(SettingsService settingsService, TrayIconManager trayIconManager)
    {
        _settingsService = settingsService;
        _trayIconManager = trayIconManager;
    }

    /// <summary>Repeats whichever mode was last used (for the global hotkey).</summary>
    public Task CaptureLastUsedMode()
    {
        var settings = _settingsService.Load();
        return settings.LastCaptureMode == CaptureMode.Region
            ? CaptureRegion()
            : CaptureFullScreen();
    }

    public async Task CaptureRegion()
    {
        if (!BeginCapture())
        {
            return;
        }

        try
        {
            var monitors = MonitorService.GetMonitors();
            var virtualDesktopBounds = MonitorService.GetVirtualDesktopBounds(monitors);
            var settings = _settingsService.Load();

            using var frozenDesktop = ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);

            var lastRegion = settings.LastRegion?.ToRectangle();
            var overlay = new RegionSelectOverlayWindow(frozenDesktop, virtualDesktopBounds, lastRegion);
            overlay.ShowDialog();

            if (overlay.Result is not { } selectedRegion)
            {
                return; // Cancelled.
            }

            using var cropped = await CaptureRegionBitmapAsync(settings.CaptureDelaySeconds, frozenDesktop, virtualDesktopBounds, selectedRegion);
            SaveAndRemember(cropped, CaptureMode.Region, lastRegion: selectedRegion);
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("Screenshot failed", ex.Message);
        }
        finally
        {
            EndCapture();
        }
    }

    public async Task CaptureFullScreen()
    {
        if (!BeginCapture())
        {
            return;
        }

        try
        {
            var monitors = MonitorService.GetMonitors();
            var virtualDesktopBounds = MonitorService.GetVirtualDesktopBounds(monitors);
            var settings = _settingsService.Load();

            using var frozenDesktop = ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);

            MonitorInfo? chosenMonitor;
            if (monitors.Count == 1)
            {
                // Single monitor: no need to bother the user with a picker.
                chosenMonitor = monitors[0];
            }
            else
            {
                var lastMonitorDeviceName = settings.LastMonitorDeviceName;
                var picker = new MonitorPickerOverlayWindow(frozenDesktop, virtualDesktopBounds, monitors, lastMonitorDeviceName);
                picker.ShowDialog();
                chosenMonitor = picker.Result;
            }

            if (chosenMonitor is null)
            {
                return; // Cancelled.
            }

            using var cropped = await CaptureMonitorBitmapAsync(settings.CaptureDelaySeconds, frozenDesktop, virtualDesktopBounds, chosenMonitor);
            SaveAndRemember(cropped, CaptureMode.FullScreen, lastMonitorDeviceName: chosenMonitor.DeviceName);
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("Screenshot failed", ex.Message);
        }
        finally
        {
            EndCapture();
        }
    }

    private void SaveAndRemember(System.Drawing.Bitmap bitmap, CaptureMode mode,
        System.Drawing.Rectangle? lastRegion = null, string? lastMonitorDeviceName = null)
    {
        var settings = _settingsService.Load();

        try
        {
            var result = ImageSaveService.Save(bitmap, settings);

            if (result.UsedFallbackFolder)
            {
                _trayIconManager.ShowFailureNotification(
                    "Saved to fallback folder",
                    $"Your configured save folder was unavailable ({result.FallbackReason}). " +
                    $"Saved to {result.SavedFilePath} instead.");
            }
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("Screenshot could not be saved", ex.Message);
            return;
        }

        // Only remember these after a successful (or fallback-successful) save, so a
        // failed capture doesn't flip the hotkey's remembered mode/region/monitor.
        settings.LastCaptureMode = mode;
        if (lastRegion is { } region)
        {
            settings.LastRegion = Models.CaptureRegion.FromRectangle(region);
        }
        if (lastMonitorDeviceName is not null)
        {
            settings.LastMonitorDeviceName = lastMonitorDeviceName;
        }
        _settingsService.Save(settings);
    }

    private bool BeginCapture()
    {
        if (_captureInProgress)
        {
            return false;
        }
        _captureInProgress = true;
        return true;
    }

    private void EndCapture() => _captureInProgress = false;

    private static async Task<System.Drawing.Bitmap> CaptureRegionBitmapAsync(
        int captureDelaySeconds,
        System.Drawing.Bitmap frozenDesktop,
        System.Drawing.Rectangle virtualDesktopBounds,
        System.Drawing.Rectangle selectedRegion)
    {
        if (captureDelaySeconds <= 0)
        {
            return ScreenCaptureService.CropRegion(frozenDesktop, virtualDesktopBounds, selectedRegion);
        }

        await Task.Delay(TimeSpan.FromSeconds(captureDelaySeconds));
        using var delayedDesktop = ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);
        return ScreenCaptureService.CropRegion(delayedDesktop, virtualDesktopBounds, selectedRegion);
    }

    private static async Task<System.Drawing.Bitmap> CaptureMonitorBitmapAsync(
        int captureDelaySeconds,
        System.Drawing.Bitmap frozenDesktop,
        System.Drawing.Rectangle virtualDesktopBounds,
        MonitorInfo chosenMonitor)
    {
        if (captureDelaySeconds <= 0)
        {
            return ScreenCaptureService.CropMonitor(frozenDesktop, virtualDesktopBounds, chosenMonitor);
        }

        await Task.Delay(TimeSpan.FromSeconds(captureDelaySeconds));
        using var delayedDesktop = ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);
        return ScreenCaptureService.CropMonitor(delayedDesktop, virtualDesktopBounds, chosenMonitor);
    }
}
