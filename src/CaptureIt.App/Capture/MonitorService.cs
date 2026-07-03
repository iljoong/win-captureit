using System.Drawing;
using CaptureIt.App.Models;

namespace CaptureIt.App.Capture;

/// <summary>
/// Enumerates the physical monitors that make up the virtual desktop. All bounds are
/// in physical pixels; monitors left of or above the primary monitor will have
/// negative coordinates, which callers must not assume away.
/// </summary>
public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<(RectangleAdapter Bounds, RectangleAdapter WorkingArea, bool IsPrimary, string DeviceName)>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>()
            };

            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add((
                    new RectangleAdapter(info.rcMonitor),
                    new RectangleAdapter(info.rcWork),
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    info.szDevice));
            }

            return true;
        }, IntPtr.Zero);

        var result = new List<MonitorInfo>(monitors.Count);
        for (var i = 0; i < monitors.Count; i++)
        {
            var (bounds, workingArea, isPrimary, deviceName) = monitors[i];
            result.Add(new MonitorInfo
            {
                Index = i + 1,
                Bounds = bounds.ToRectangle(),
                WorkingArea = workingArea.ToRectangle(),
                IsPrimary = isPrimary,
                DeviceName = deviceName
            });
        }

        // Ensure primary monitor is listed first / numbered predictably for the picker overlay.
        return result
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.Left)
            .Select((m, idx) => new MonitorInfo
            {
                Index = idx + 1,
                Bounds = m.Bounds,
                WorkingArea = m.WorkingArea,
                IsPrimary = m.IsPrimary,
                DeviceName = m.DeviceName
            })
            .ToList();
    }

    /// <summary>Bounding rectangle of the full virtual desktop (union of all monitors).</summary>
    public static Rectangle GetVirtualDesktopBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
        {
            return Rectangle.Empty;
        }

        int left = monitors.Min(m => m.Bounds.Left);
        int top = monitors.Min(m => m.Bounds.Top);
        int right = monitors.Max(m => m.Bounds.Right);
        int bottom = monitors.Max(m => m.Bounds.Bottom);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    /// <summary>Small helper to convert the native RECT struct to System.Drawing.Rectangle.</summary>
    private readonly struct RectangleAdapter
    {
        private readonly int _left, _top, _right, _bottom;

        public RectangleAdapter(NativeMethods.RECT rect)
        {
            _left = rect.Left;
            _top = rect.Top;
            _right = rect.Right;
            _bottom = rect.Bottom;
        }

        public int Left => _left;

        public Rectangle ToRectangle() => Rectangle.FromLTRB(_left, _top, _right, _bottom);
    }
}
