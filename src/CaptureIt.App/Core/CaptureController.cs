using CaptureIt.App.Capture;
using CaptureIt.App.Models;
using CaptureIt.App.Overlays;
using CaptureIt.App.Settings;
using CaptureIt.App.TrayIcon;
using System.IO;

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
    public void CaptureLastUsedMode()
    {
        var settings = _settingsService.Load();
        if (settings.LastCaptureMode == CaptureMode.Region)
        {
            CaptureRegion();
        }
        else
        {
            CaptureFullScreen();
        }
    }

    public void CaptureRegion()
    {
        if (!BeginCapture())
        {
            return;
        }

        try
        {
            var monitors = MonitorService.GetMonitors();
            var virtualDesktopBounds = MonitorService.GetVirtualDesktopBounds(monitors);

            // If a delay is configured, count down first (letting the user set up
            // transient UI), then freeze the desktop — the frozen image is the moment
            // just before the overlay appears, so the transient UI is included but the
            // countdown/overlay never leak into the shot.
            CountdownOverlayWindow.Run(_settingsService.Load().CaptureDelaySeconds);

            using var frozenDesktop = ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);

            var lastRegion = _settingsService.Load().LastRegion?.ToRectangle();
            var overlay = new RegionSelectOverlayWindow(frozenDesktop, virtualDesktopBounds, lastRegion);
            overlay.ShowDialog();

            if (overlay.Result is not { } selectedRegion)
            {
                return; // Cancelled.
            }

            using var cropped = ScreenCaptureService.CropRegion(frozenDesktop, virtualDesktopBounds, selectedRegion);
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

    public void CaptureFullScreen()
    {
        if (!BeginCapture())
        {
            return;
        }

        try
        {
            var monitors = MonitorService.GetMonitors();
            var virtualDesktopBounds = MonitorService.GetVirtualDesktopBounds(monitors);

            // If a delay is configured, count down first (letting the user set up
            // transient UI), then freeze the desktop — the frozen image is the moment
            // just before the overlay appears, so the transient UI is included but the
            // countdown/overlay never leak into the shot.
            CountdownOverlayWindow.Run(_settingsService.Load().CaptureDelaySeconds);

            using var frozenDesktop = ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);

            MonitorInfo? chosenMonitor;
            if (monitors.Count == 1)
            {
                // Single monitor: no need to bother the user with a picker.
                chosenMonitor = monitors[0];
            }
            else
            {
                var lastMonitorDeviceName = _settingsService.Load().LastMonitorDeviceName;
                var picker = new MonitorPickerOverlayWindow(frozenDesktop, virtualDesktopBounds, monitors, lastMonitorDeviceName);
                picker.ShowDialog();
                chosenMonitor = picker.Result;
            }

            if (chosenMonitor is null)
            {
                return; // Cancelled.
            }

            using var cropped = ScreenCaptureService.CropMonitor(frozenDesktop, virtualDesktopBounds, chosenMonitor);
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
            if (settings.SaveToClipboard)
            {
                if (!CopyToClipboard(bitmap, settings))
                {
                    return; // Nothing was placed on the clipboard; don't update remembered state.
                }
            }
            else
            {
                var result = ImageSaveService.Save(bitmap, settings);

                if (result.UsedFallbackFolder)
                {
                    _trayIconManager.ShowFailureNotification(
                        "Saved to fallback folder",
                        $"Your configured save folder was unavailable ({result.FallbackReason}). " +
                        $"Saved to {result.SavedFilePath} instead.");
                }

                if (settings.OcrEnabled)
                {
                    RunOcrAndSave(bitmap, result.SavedFilePath);
                }
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

    /// <summary>
    /// Runs OCR on the just-saved bitmap and writes the recognized text next to the
    /// image, reusing its base file name with a .txt extension. OCR failures (e.g. no
    /// language pack installed) are reported but don't affect the already-successful
    /// image save.
    /// </summary>
    private void RunOcrAndSave(System.Drawing.Bitmap bitmap, string savedImagePath)
    {
        try
        {
            var text = OcrService.ExtractText(bitmap);
            if (text is not null)
            {
                var textFilePath = Path.ChangeExtension(savedImagePath, ".txt");
                File.WriteAllText(textFilePath, text);
            }
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("OCR failed", ex.Message);
        }
    }

    /// <summary>
    /// Copies the capture result to the clipboard instead of saving a file. When OCR is
    /// enabled, the recognized text is copied; otherwise the image is copied. Returns
    /// false (after notifying the user) when OCR is enabled but no text could be
    /// recognized, so there is nothing to place on the clipboard.
    /// </summary>
    private bool CopyToClipboard(System.Drawing.Bitmap bitmap, Models.AppSettings settings)
    {
        try
        {
            if (settings.OcrEnabled)
            {
                var text = OcrService.ExtractText(bitmap);
                if (string.IsNullOrEmpty(text))
                {
                    _trayIconManager.ShowFailureNotification(
                        "No text to copy",
                        "OCR did not find any text in the capture, so nothing was copied to the clipboard.");
                    return false;
                }

                ClipboardService.SetText(text);
                return true;
            }

            ClipboardService.SetImage(bitmap);
            return true;
        }
        catch (Exception ex)
        {
            // Clipboard access can fail transiently (e.g. it's locked by another app).
            _trayIconManager.ShowFailureNotification("Could not copy to clipboard", ex.Message);
            return false;
        }
    }


    /// <summary>
    /// When a capture delay is configured, shows the countdown overlay (giving the
    /// user time to set up a transient UI state) and then re-captures the live desktop
    /// so that state is included in the screenshot. Returns the fresh capture, which
    /// the caller owns and must dispose; returns null when no delay is configured, in
    /// which case the caller should crop the already-frozen desktop instead.
    /// </summary>
    private static System.Drawing.Bitmap? ApplyCaptureDelay(int delaySeconds, System.Drawing.Rectangle virtualDesktopBounds)
    {
        if (delaySeconds <= 0)
        {
            return null;
        }

        Overlays.CountdownOverlayWindow.Run(delaySeconds);
        return ScreenCaptureService.CaptureVirtualDesktop(virtualDesktopBounds);
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
}
