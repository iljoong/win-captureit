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

    private AiAnswerOverlayWindow(Bitmap bitmap, AppSettings settings)
    {
        InitializeComponent();

        _bitmap = bitmap;
        _settings = settings;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Same reasoning as the other overlays: this is triggered from a background
        // tray app, so it needs to force itself to the foreground to receive
        // keyboard input (Enter/Esc) without a preceding click.
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeWindowPositioning.ForceForeground(hwnd);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
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
    /// overlays) until the user dismisses it with Enter or Esc.
    /// </summary>
    public static void ShowAnswer(Bitmap bitmap, AppSettings settings)
    {
        var overlay = new AiAnswerOverlayWindow(bitmap, settings);
        overlay.ShowDialog();
    }
}
