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

            var settings = _settingsService.Load();
            ProcessCaptureResult(cropped, settings, CaptureMode.Region, selectedRegion,
                lastRegion: selectedRegion);
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

            var settings = _settingsService.Load();
            ProcessCaptureResult(cropped, settings, CaptureMode.FullScreen, chosenMonitor.Bounds,
                lastMonitorDeviceName: chosenMonitor.DeviceName);
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

    /// <summary>
    /// Runs the shared post-capture pipeline: shows the AI answer overlay when the
    /// "Use AI to answer" engine is selected, extracts text if enabled, and stores the
    /// result (image and/or text) to file and/or clipboard per the saving option.
    /// Saving is independent of the extraction engine — an AI answer is saved just like
    /// any other capture — and the capture state is remembered only after a successful
    /// save (or when saving is off). <paramref name="bounds"/> is the selected region's
    /// (or captured monitor's) physical-pixel bounds, used to center the answer overlay.
    /// </summary>
    private void ProcessCaptureResult(System.Drawing.Bitmap bitmap, AppSettings settings, CaptureMode mode,
        System.Drawing.Rectangle bounds, System.Drawing.Rectangle? lastRegion = null, string? lastMonitorDeviceName = null)
    {
        // The AI answer overlay is the on-screen display for "Use AI to answer"; it runs
        // regardless of the saving option. Its answer is captured so it can also be saved.
        string? aiAnswer = null;
        if (settings.OcrEnabled && settings.AiCapture.Mode == AiCaptureMode.Answer)
        {
            aiAnswer = RunAiAnswerFlow(bitmap, settings, bounds);
        }

        try
        {
            // Run the (potentially slow) save/extraction on a background STA thread
            // while a "Saving…" spinner gives the user feedback, so they aren't left
            // wondering during a long OCR/AI save.
            SavingProgressOverlayWindow.RunWhileSaving(() => SaveCapture(bitmap, settings, aiAnswer));
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("Screenshot could not be saved", ex.Message);
            return;
        }

        // Only remember these after a successful save, so a failed capture doesn't flip
        // the hotkey's remembered mode/region/monitor.
        RememberCaptureState(mode, lastRegion, lastMonitorDeviceName, settings);
    }

    /// <summary>
    /// Shows the AI-generated answer overlay for "Use AI to answer" mode over
    /// <paramref name="bounds"/> and returns the answer text so the caller can save it.
    /// The capture is used as context for the AI call and the result is shown on screen
    /// until the user dismisses it (Enter/Esc).
    /// </summary>
    private string? RunAiAnswerFlow(System.Drawing.Bitmap bitmap, AppSettings settings, System.Drawing.Rectangle bounds)
    {
        try
        {
            return AiAnswerOverlayWindow.ShowAnswer(bitmap, settings, _settingsService, bounds);
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("AI capture failed", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Routes the capture to file and/or clipboard per <see cref="AppSettings.Saving"/>.
    /// When text extraction is enabled it is run once (via the selected engine) and the
    /// resulting text is written as a sidecar file (for file saves) and/or placed on the
    /// clipboard in place of the image.
    /// </summary>
    private void SaveCapture(System.Drawing.Bitmap bitmap, AppSettings settings, string? aiAnswer)
    {
        if (settings.Saving == SavingOption.Off)
        {
            return;
        }

        // Extract once (if enabled) and reuse for both file and clipboard destinations.
        string? extractedText = settings.OcrEnabled ? ExtractText(bitmap, settings, aiAnswer) : null;

        if (settings.Saving.SavesToFile())
        {
            SaveToFile(bitmap, settings, extractedText);
        }

        if (settings.Saving.SavesToClipboard())
        {
            SaveToClipboard(bitmap, settings, extractedText);
        }
    }

    /// <summary>
    /// Runs the configured text-extraction engine on the capture. For "Use AI to answer"
    /// it combines the extracted formatted text with the AI's answer. Failures are
    /// reported but return null so the image can still be saved.
    /// </summary>
    private string? ExtractText(System.Drawing.Bitmap bitmap, AppSettings settings, string? aiAnswer)
    {
        try
        {
            return settings.AiCapture.Mode switch
            {
                AiCaptureMode.WindowsOcr => OcrService.ExtractText(bitmap),
                AiCaptureMode.Capture => AiCaptureService.ExtractMarkdown(bitmap, settings),
                AiCaptureMode.Answer => CombineExtractedTextAndAnswer(
                    AiCaptureService.ExtractMarkdown(bitmap, settings), aiAnswer),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _trayIconManager.ShowFailureNotification("Text extraction failed", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Combines the extracted formatted (Markdown) text and the AI answer into a single
    /// document (used by "Use AI to answer" for both the sidecar file and the clipboard).
    /// </summary>
    private static string CombineExtractedTextAndAnswer(string? extractedText, string? answer)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            sb.AppendLine("# Extracted text");
            sb.AppendLine();
            sb.AppendLine(extractedText.Trim());
        }
        if (!string.IsNullOrWhiteSpace(answer))
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("# Answer");
            sb.AppendLine();
            sb.AppendLine(answer.Trim());
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Saves the capture image to disk and, when text was extracted, writes it as a
    /// sidecar file next to the image (.txt for Windows OCR, .md for the AI engines).
    /// </summary>
    private void SaveToFile(System.Drawing.Bitmap bitmap, AppSettings settings, string? extractedText)
    {
        var result = ImageSaveService.Save(bitmap, settings);

        if (result.UsedFallbackFolder)
        {
            _trayIconManager.ShowFailureNotification(
                "Saved to fallback folder",
                $"Your configured save folder was unavailable ({result.FallbackReason}). " +
                $"Saved to {result.SavedFilePath} instead.");
        }

        if (settings.OcrEnabled && !string.IsNullOrEmpty(extractedText))
        {
            try
            {
                var extension = settings.AiCapture.Mode == AiCaptureMode.WindowsOcr ? ".txt" : ".md";
                var textFilePath = Path.ChangeExtension(result.SavedFilePath, extension);
                File.WriteAllText(textFilePath, extractedText);
            }
            catch (Exception ex)
            {
                _trayIconManager.ShowFailureNotification("Extracted text could not be saved", ex.Message);
            }
        }
    }

    /// <summary>
    /// Places the capture on the clipboard: the extracted text when text extraction is
    /// enabled, otherwise the image.
    /// </summary>
    private void SaveToClipboard(System.Drawing.Bitmap bitmap, AppSettings settings, string? extractedText)
    {
        try
        {
            if (settings.OcrEnabled)
            {
                if (string.IsNullOrEmpty(extractedText))
                {
                    _trayIconManager.ShowFailureNotification(
                        "No text to copy",
                        "Text extraction did not return anything, so nothing was copied to the clipboard.");
                    return;
                }

                ClipboardService.SetText(extractedText);
            }
            else
            {
                ClipboardService.SetImage(bitmap);
            }
        }
        catch (Exception ex)
        {
            // Clipboard access can fail transiently (e.g. it's locked by another app).
            _trayIconManager.ShowFailureNotification("Could not copy to clipboard", ex.Message);
        }
    }

    private void RememberCaptureState(CaptureMode mode, System.Drawing.Rectangle? lastRegion = null,
        string? lastMonitorDeviceName = null, AppSettings? loadedSettings = null)
    {
        var settings = loadedSettings ?? _settingsService.Load();
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
