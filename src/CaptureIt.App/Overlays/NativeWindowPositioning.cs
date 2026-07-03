using System.Drawing;
using System.Runtime.InteropServices;

namespace CaptureIt.App.Overlays;

/// <summary>
/// Positions/sizes a window directly in physical screen pixels via SetWindowPos,
/// bypassing WPF's Left/Top/Width/Height (which are expressed in DPI-dependent
/// device-independent units). This is what lets overlay windows exactly span a
/// virtual-desktop rectangle regardless of per-monitor DPI scaling.
/// </summary>
internal static class NativeWindowPositioning
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public static void SetWindowBoundsPhysicalPixels(IntPtr hwnd, Rectangle boundsInPhysicalPixels)
    {
        // Note: SWP_NOACTIVATE is deliberately NOT used. These overlays are triggered
        // from a background tray app (global hotkey / tray menu); without activation
        // the window shows but never receives keyboard focus, so Enter/Esc/number-key
        // shortcuts silently do nothing. ForceForeground below completes the job when
        // Windows' foreground lock would otherwise block a background process.
        SetWindowPos(
            hwnd, IntPtr.Zero,
            boundsInPhysicalPixels.Left, boundsInPhysicalPixels.Top,
            boundsInPhysicalPixels.Width, boundsInPhysicalPixels.Height,
            SWP_NOZORDER | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Reliably makes the given window the foreground/active window so it receives
    /// keyboard input. Uses the AttachThreadInput trick to bypass Windows' foreground-
    /// lock, which otherwise prevents a background process (our tray app, activated by
    /// a global hotkey) from stealing focus from the currently-active application.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint currentThread = GetCurrentThreadId();

        if (foregroundThread != currentThread && foreground != IntPtr.Zero)
        {
            AttachThreadInput(foregroundThread, currentThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            AttachThreadInput(foregroundThread, currentThread, false);
        }
        else
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
    }
}
