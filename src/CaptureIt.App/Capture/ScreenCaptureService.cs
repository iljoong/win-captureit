using System.Drawing;
using System.Drawing.Imaging;
using CaptureIt.App.Models;

namespace CaptureIt.App.Capture;

/// <summary>
/// Captures the entire virtual desktop into a single frozen bitmap using GDI
/// (BitBlt). "Freeze-first": the whole desktop is captured once, before any overlay
/// UI is shown, and all subsequent region/monitor crops are taken from that single
/// frozen bitmap. This avoids the overlay (dimming, picker numbers, selection
/// rectangle) ever leaking into the final screenshot, and avoids timing races from
/// re-capturing after the user interacts with an overlay.
///
/// Known limitations (GDI capture, documented for v1):
/// - Windows showing DRM-protected content (e.g. some video players) may render as
///   black in the capture.
/// - The UAC secure desktop cannot be captured.
/// </summary>
public static class ScreenCaptureService
{
    /// <summary>
    /// Captures the full virtual desktop (union of all monitors) as a single bitmap,
    /// in physical pixels. The caller owns the returned bitmap and must dispose it.
    /// </summary>
    public static Bitmap CaptureVirtualDesktop(Rectangle virtualDesktopBounds)
    {
        IntPtr desktopWnd = NativeMethods.GetDesktopWindow();
        IntPtr desktopDc = NativeMethods.GetWindowDC(desktopWnd);
        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmapHandle = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(desktopDc);
            bitmapHandle = NativeMethods.CreateCompatibleBitmap(
                desktopDc, virtualDesktopBounds.Width, virtualDesktopBounds.Height);

            IntPtr oldBitmap = NativeMethods.SelectObject(memoryDc, bitmapHandle);
            try
            {
                NativeMethods.BitBlt(
                    memoryDc, 0, 0, virtualDesktopBounds.Width, virtualDesktopBounds.Height,
                    desktopDc, virtualDesktopBounds.Left, virtualDesktopBounds.Top,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            }
            finally
            {
                NativeMethods.SelectObject(memoryDc, oldBitmap);
            }

            // Image.FromHbitmap copies the pixel data into a managed Bitmap, so it's
            // safe to delete the native bitmap handle afterwards.
            return Image.FromHbitmap(bitmapHandle);
        }
        finally
        {
            if (bitmapHandle != IntPtr.Zero) NativeMethods.DeleteObject(bitmapHandle);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            if (desktopDc != IntPtr.Zero) NativeMethods.ReleaseDC(desktopWnd, desktopDc);
        }
    }

    /// <summary>
    /// Crops a region from a frozen full-desktop bitmap. <paramref name="regionInVirtualDesktopCoords"/>
    /// must be in the same physical-pixel coordinate space as <paramref name="virtualDesktopBounds"/>
    /// used to capture <paramref name="frozenDesktop"/>.
    /// </summary>
    public static Bitmap CropRegion(Bitmap frozenDesktop, Rectangle virtualDesktopBounds, Rectangle regionInVirtualDesktopCoords)
    {
        // Translate from virtual-desktop coordinates (which may be negative) into
        // the frozen bitmap's local (0,0)-based coordinates.
        var local = new Rectangle(
            regionInVirtualDesktopCoords.Left - virtualDesktopBounds.Left,
            regionInVirtualDesktopCoords.Top - virtualDesktopBounds.Top,
            regionInVirtualDesktopCoords.Width,
            regionInVirtualDesktopCoords.Height);

        // Clamp to the bitmap bounds defensively in case of off-by-one/rounding at edges.
        local = Rectangle.Intersect(local, new Rectangle(Point.Empty, frozenDesktop.Size));
        if (local.Width <= 0 || local.Height <= 0)
        {
            throw new InvalidOperationException("Selected region is empty or outside the captured desktop bounds.");
        }

        return frozenDesktop.Clone(local, PixelFormat.Format32bppArgb);
    }

    /// <summary>Crops the given monitor's full bounds out of the frozen desktop bitmap.</summary>
    public static Bitmap CropMonitor(Bitmap frozenDesktop, Rectangle virtualDesktopBounds, MonitorInfo monitor)
        => CropRegion(frozenDesktop, virtualDesktopBounds, monitor.Bounds);
}
