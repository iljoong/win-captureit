using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CaptureIt.App.Overlays;

/// <summary>
/// A small, non-interactive "Saving…" spinner shown while the (potentially slow)
/// save/text-extraction pipeline runs, so the user gets feedback that the capture is
/// still being processed instead of experiencing an unexplained delay.
///
/// Like <see cref="CountdownOverlayWindow"/>, the window is click-through
/// (WS_EX_TRANSPARENT) and never activated (WS_EX_NOACTIVATE), so it never steals
/// focus from whatever the user is doing. Use the static <see cref="RunWhileSaving"/>
/// helper, which runs the work on a background STA thread (keeping the UI thread free
/// so the indeterminate spinner keeps animating and clipboard access — which requires
/// STA — still works) while pumping the message loop until the work completes.
/// </summary>
public partial class SavingProgressOverlayWindow : Window
{
    // Only pop the spinner once the save has clearly taken a moment, so instant
    // (e.g. PNG-only, no text extraction) saves never flash it.
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(250);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private SavingProgressOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Make the window click-through and non-activating so it never steals focus.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a background STA thread while showing the
    /// "Saving…" spinner, and returns only once the work has completed and the overlay
    /// has been removed from the screen. The UI message loop is pumped throughout (via
    /// a nested <see cref="DispatcherFrame"/>) so the spinner animates and the tray app
    /// stays responsive. Any exception thrown by <paramref name="work"/> is re-thrown
    /// to the caller (preserving its stack trace), so existing error handling is kept.
    /// Must be called on the UI thread.
    /// </summary>
    public static void RunWhileSaving(Action work)
    {
        var uiDispatcher = Dispatcher.CurrentDispatcher;
        var overlay = new SavingProgressOverlayWindow();
        var frame = new DispatcherFrame();
        ExceptionDispatchInfo? error = null;
        var completed = false;

        var showTimer = new DispatcherTimer(DispatcherPriority.Normal, uiDispatcher)
        {
            Interval = ShowDelay
        };
        showTimer.Tick += (_, _) =>
        {
            showTimer.Stop();
            if (!completed)
            {
                overlay.Show();
            }
        };
        showTimer.Start();

        var worker = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                uiDispatcher.BeginInvoke(() =>
                {
                    completed = true;
                    showTimer.Stop();
                    if (overlay.IsVisible)
                    {
                        overlay.Close();
                    }
                    frame.Continue = false;
                });
            }
        })
        {
            IsBackground = true,
            Name = "CaptureIt.SaveCapture"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        Dispatcher.PushFrame(frame);

        // Flush any pending render/close work so the overlay's pixels are gone before
        // control returns to the caller.
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

        error?.Throw();
    }
}
