using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CaptureIt.App.Capture;
using CaptureIt.App.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace CaptureIt.App.Overlays;

/// <summary>
/// Displays the result of "Use AI to answer" mode as an always-on-top overlay
/// instead of saving the capture. Shows a "Thinking…" placeholder immediately,
/// kicks off the AI call in the background, and updates the text in place once the
/// answer arrives (or an error occurs). Dismissed with Enter or Esc, per spec.
/// </summary>
public partial class AiAnswerOverlayWindow : Window
{
    private readonly Bitmap _bitmap;
    private readonly AppSettings _settings;
    private readonly System.Drawing.Rectangle _targetBounds;

    private AiAnswerOverlayWindow(Bitmap bitmap, AppSettings settings, System.Drawing.Rectangle targetBounds)
    {
        InitializeComponent();

        _bitmap = bitmap;
        _settings = settings;
        _targetBounds = targetBounds;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Center the overlay within the selected region (or captured monitor)
        // rather than always on the primary screen, using physical pixels so it
        // lands correctly regardless of per-monitor DPI scaling.
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyCenteredBounds(hwnd);

        // Same reasoning as the other overlays: this is triggered from a background
        // tray app, so it needs to force itself to the foreground to receive
        // keyboard input (Enter/Esc) without a preceding click.
        NativeWindowPositioning.ForceForeground(hwnd);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    /// <summary>
    /// Computes and applies the centered window rect for <see cref="_targetBounds"/>.
    /// Split out so it can be re-applied in <see cref="OnLoaded"/>: when the window
    /// declares a fixed DIP size (as this one does), moving it cross-monitor via
    /// <see cref="NativeWindowPositioning.SetWindowBoundsPhysicalPixels"/> triggers
    /// WM_DPICHANGED, and WPF's built-in per-monitor-DPI handling reacts by
    /// resizing/repositioning the window itself (anchored to the window's *previous*
    /// top-left, not our centered target) whenever the destination monitor's DPI
    /// differs from the one the window was created on. That silently undoes our
    /// centering — reliably reproducible when centering on any monitor other than
    /// the one the window happened to be created on. Re-running this after the
    /// window has settled (in Loaded) corrects any such drift.
    /// </summary>
    private void ApplyCenteredBounds(IntPtr hwnd)
    {
        var bounds = NativeWindowPositioning.GetCenteredPhysicalBounds(_targetBounds, Width, Height);
        NativeWindowPositioning.SetWindowBoundsPhysicalPixels(hwnd, bounds);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-apply centering now that any WM_DPICHANGED-driven adjustment from
        // SourceInitialized's initial move has already settled (see ApplyCenteredBounds).
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyCenteredBounds(hwnd);

        try
        {
            var answer = await AiCaptureService.AnswerAsync(_bitmap, _settings);
            AnswerText.Text = string.IsNullOrWhiteSpace(answer) ? "(No answer returned.)" : answer;
        }
        catch (Exception ex)
        {
            AnswerText.Text = $"AI capture failed: {ex.Message}";
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// Shows the overlay and blocks (pumping the message loop, like the other
    /// overlays) until the user dismisses it with Enter or Esc. <paramref name="targetBounds"/>
    /// is the selected region's (or captured monitor's) bounds in physical pixels,
    /// used to center the overlay over the relevant area of the screen.
    /// </summary>
    public static void ShowAnswer(Bitmap bitmap, AppSettings settings, System.Drawing.Rectangle targetBounds)
    {
        var overlay = new AiAnswerOverlayWindow(bitmap, settings, targetBounds);
        overlay.ShowDialog();
    }
}
