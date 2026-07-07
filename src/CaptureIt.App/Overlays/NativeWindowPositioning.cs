using System.Drawing;
using System.Runtime.InteropServices;
using CaptureIt.App.Capture;

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
    private static extern IntPtr MonitorFromRect(ref NativeMethods.RECT lprc, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

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
    private const uint SWP_NOSIZE = 0x0001;

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
    /// Moves a window's top-left to the given physical-pixel position without
    /// changing its size (SWP_NOSIZE). Used for fixed-size WPF windows where WPF
    /// itself owns the (DPI-scaled) size — we must only reposition, never resize,
    /// so we don't desync WPF's DIP-based sizing and trigger runaway DPI rescaling.
    /// </summary>
    public static void SetWindowPositionPhysicalPixels(IntPtr hwnd, int x, int y)
    {
        SetWindowPos(
            hwnd, IntPtr.Zero,
            x, y, 0, 0,
            SWP_NOZORDER | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Computes a window rectangle, in physical pixels, sized from the given
    /// device-independent (WPF) dimensions — scaled by the DPI of the monitor
    /// nearest <paramref name="targetBoundsPhysicalPixels"/> — and centered within
    /// that area (e.g. the selected region or the captured monitor). The result is
    /// clamped so the window never exceeds the target area, keeping it fully
    /// visible even when centered on a small selected region.
    /// </summary>
    public static Rectangle GetCenteredPhysicalBounds(
        Rectangle targetBoundsPhysicalPixels, double desiredWidthDip, double desiredHeightDip)
    {
        double dpiScale = 1.0;
        var rect = new NativeMethods.RECT
        {
            Left = targetBoundsPhysicalPixels.Left,
            Top = targetBoundsPhysicalPixels.Top,
            Right = targetBoundsPhysicalPixels.Right,
            Bottom = targetBoundsPhysicalPixels.Bottom
        };

        var hMonitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
        {
            dpiScale = dpiX / 96.0;
        }

        int width = (int)Math.Round(desiredWidthDip * dpiScale);
        int height = (int)Math.Round(desiredHeightDip * dpiScale);

        const int margin = 40;
        width = Math.Min(width, Math.Max(200, targetBoundsPhysicalPixels.Width - margin));
        height = Math.Min(height, Math.Max(150, targetBoundsPhysicalPixels.Height - margin));

        int x = targetBoundsPhysicalPixels.Left + (targetBoundsPhysicalPixels.Width - width) / 2;
        int y = targetBoundsPhysicalPixels.Top + (targetBoundsPhysicalPixels.Height - height) / 2;

        return new Rectangle(x, y, width, height);
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
