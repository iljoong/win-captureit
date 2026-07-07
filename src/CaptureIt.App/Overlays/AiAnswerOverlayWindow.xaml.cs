using System.Drawing;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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
    private const int WM_DPICHANGED = 0x02E0;

    private readonly Bitmap _bitmap;
    private readonly AppSettings _settings;
    private readonly System.Drawing.Rectangle _targetBounds;

    // The overlay's intended size in device-independent (WPF) units, captured from
    // XAML before the window is shown. Used to compute the centered position; WPF
    // itself owns the actual (DPI-scaled) window size, so these never change even as
    // the window moves between monitors of differing DPI.
    private readonly double _intendedWidthDip;
    private readonly double _intendedHeightDip;

    private AiAnswerOverlayWindow(Bitmap bitmap, AppSettings settings, System.Drawing.Rectangle targetBounds)
    {
        InitializeComponent();

        _bitmap = bitmap;
        _settings = settings;
        _targetBounds = targetBounds;
        _intendedWidthDip = Width;
        _intendedHeightDip = Height;

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

        // Watch for WM_DPICHANGED so we can re-assert our centered position after WPF's
        // built-in per-monitor-DPI handling reacts (see ApplyCenteredPosition / WndProc).
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        ApplyCenteredPosition(hwnd);

        // Same reasoning as the other overlays: this is triggered from a background
        // tray app, so it needs to force itself to the foreground to receive
        // keyboard input (Enter/Esc) without a preceding click.
        NativeWindowPositioning.ForceForeground(hwnd);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    /// <summary>
    /// Re-centers the overlay over <see cref="_targetBounds"/> by setting only its
    /// top-left position in physical pixels — never its size. This window has a fixed
    /// DIP size (760x520) that WPF renders at the correct physical size for whichever
    /// monitor it currently occupies, so we must let WPF own sizing. Earlier attempts
    /// that also set the physical *size* desynced WPF's DIP model: moving the window
    /// onto a monitor with a different DPI fires WM_DPICHANGED, and WPF re-scales the
    /// window by the DPI ratio — compounding with our manual size and shrinking (or
    /// growing) the overlay on every transition. By repositioning only, WPF keeps the
    /// size correct and we just keep it centered. The centered top-left is derived
    /// from the intended DIP size scaled by the *target* monitor's DPI (via
    /// <see cref="NativeWindowPositioning.GetCenteredPhysicalBounds"/>), matching the
    /// physical size WPF will render there. Re-applied after WM_DPICHANGED settles
    /// (from <see cref="WndProc"/>) and in <see cref="OnLoaded"/> to correct the
    /// proportional reposition WPF applies during the DPI transition.
    /// </summary>
    private void ApplyCenteredPosition(IntPtr hwnd)
    {
        var bounds = NativeWindowPositioning.GetCenteredPhysicalBounds(
            _targetBounds, _intendedWidthDip, _intendedHeightDip);
        NativeWindowPositioning.SetWindowPositionPhysicalPixels(hwnd, bounds.Left, bounds.Top);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DPICHANGED)
        {
            // Let WPF apply its DPI-driven resize/reposition first, then re-center the
            // (correctly WPF-sized) window over the target once that has settled.
            // Repositioning within the same monitor does not fire another
            // WM_DPICHANGED, so this cannot loop.
            Dispatcher.BeginInvoke(new Action(() => ApplyCenteredPosition(hwnd)), DispatcherPriority.Loaded);
        }

        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-center now that any WM_DPICHANGED-driven adjustment from
        // SourceInitialized's initial move has already settled (see ApplyCenteredPosition).
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyCenteredPosition(hwnd);

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
