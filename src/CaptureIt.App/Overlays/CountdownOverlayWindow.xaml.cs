using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CaptureIt.App.Overlays;

/// <summary>
/// A small, non-interactive countdown overlay shown at the very start of a capture,
/// before the desktop is frozen and the region/monitor overlay appears. It gives the
/// user a visible countdown while they set up a transient UI state (open a menu,
/// hover a tooltip, etc.).
///
/// The window is deliberately click-through (WS_EX_TRANSPARENT) and never activated
/// (WS_EX_NOACTIVATE), so the user keeps focus on whatever app they're preparing —
/// the countdown must not steal keyboard/mouse focus from the transient UI being set
/// up. It is closed before the desktop is frozen, so it never leaks into the final
/// capture. Use the static <see cref="Run"/> helper, which shows the overlay and
/// pumps the message loop (keeping the tray app responsive / non-blocking) until the
/// countdown elapses.
/// </summary>
public partial class CountdownOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _timer;
    private int _remainingSeconds;

    private CountdownOverlayWindow(int seconds)
    {
        InitializeComponent();

        _remainingSeconds = seconds;
        CountdownText.Text = _remainingSeconds.ToString();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => _timer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Make the window click-through and non-activating so it never steals focus
        // from the app whose transient UI the user is setting up during the countdown.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            Close();
            return;
        }

        CountdownText.Text = _remainingSeconds.ToString();
    }

    /// <summary>
    /// Shows the countdown overlay for the given number of seconds and returns only
    /// after it has counted down and been removed from the screen. The message loop
    /// is pumped throughout (via a nested <see cref="DispatcherFrame"/>) so the tray
    /// app stays responsive and the user can interact with other windows to prepare
    /// their screen. Must be called on the UI thread.
    /// </summary>
    public static void Run(int seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        var overlay = new CountdownOverlayWindow(seconds);
        var frame = new DispatcherFrame();
        overlay.Closed += (_, _) => frame.Continue = false;
        overlay.Show();

        Dispatcher.PushFrame(frame);

        // The window is closed by the time we get here; flush any pending render/close
        // work at Background priority so the overlay's pixels are gone from the screen
        // before the caller BitBlts the (now transient-UI-bearing) desktop.
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
