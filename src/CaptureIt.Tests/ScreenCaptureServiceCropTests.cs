using System.Drawing;
using CaptureIt.App.Capture;
using CaptureIt.App.Models;
using Xunit;

namespace CaptureIt.Tests;

/// <summary>
/// Tests the pure coordinate math used when cropping the frozen full-desktop bitmap
/// down to a selected region or a specific monitor. Deliberately covers negative
/// virtual-desktop coordinates (monitors positioned left of/above the primary
/// monitor), which is the most common source of multi-monitor capture bugs.
/// </summary>
public class ScreenCaptureServiceCropTests
{
    [Fact]
    public void CropRegion_WithPositiveCoordinates_ExtractsExpectedArea()
    {
        var virtualDesktopBounds = new Rectangle(0, 0, 1000, 800);
        using var frozen = new Bitmap(1000, 800);

        var region = new Rectangle(100, 50, 200, 150);
        using var cropped = ScreenCaptureService.CropRegion(frozen, virtualDesktopBounds, region);

        Assert.Equal(200, cropped.Width);
        Assert.Equal(150, cropped.Height);
    }

    [Fact]
    public void CropRegion_WithNegativeVirtualDesktopOrigin_TranslatesCorrectly()
    {
        // Simulates a secondary monitor positioned to the left of the primary,
        // so the virtual desktop's top-left is negative.
        var virtualDesktopBounds = new Rectangle(-1920, 0, 3840, 1080);
        using var frozen = new Bitmap(3840, 1080);

        // A region entirely within the left (negative-coordinate) monitor.
        var region = new Rectangle(-1920, 0, 500, 400);
        using var cropped = ScreenCaptureService.CropRegion(frozen, virtualDesktopBounds, region);

        Assert.Equal(500, cropped.Width);
        Assert.Equal(400, cropped.Height);
    }

    [Fact]
    public void CropRegion_RegionEntirelyOutsideBounds_ThrowsRatherThanReturningGarbage()
    {
        var virtualDesktopBounds = new Rectangle(0, 0, 1000, 800);
        using var frozen = new Bitmap(1000, 800);

        var region = new Rectangle(5000, 5000, 100, 100);

        Assert.Throws<InvalidOperationException>(() =>
            ScreenCaptureService.CropRegion(frozen, virtualDesktopBounds, region));
    }

    [Fact]
    public void CropMonitor_UsesMonitorBoundsDirectly()
    {
        var virtualDesktopBounds = new Rectangle(0, 0, 2560, 1080);
        using var frozen = new Bitmap(2560, 1080);

        var monitor = new MonitorInfo
        {
            Index = 2,
            Bounds = new Rectangle(1920, 0, 640, 1080),
            WorkingArea = new Rectangle(1920, 0, 640, 1040),
            IsPrimary = false,
            DeviceName = "\\\\.\\DISPLAY2"
        };

        using var cropped = ScreenCaptureService.CropMonitor(frozen, virtualDesktopBounds, monitor);

        Assert.Equal(640, cropped.Width);
        Assert.Equal(1080, cropped.Height);
    }

    [Fact]
    public void GetVirtualDesktopBounds_UnionsAllMonitorsIncludingNegativeCoordinates()
    {
        var monitors = new[]
        {
            new MonitorInfo { Index = 1, Bounds = new Rectangle(0, 0, 1920, 1080), IsPrimary = true },
            new MonitorInfo { Index = 2, Bounds = new Rectangle(-1080, 0, 1080, 1920), IsPrimary = false } // portrait monitor to the left
        };

        var bounds = MonitorService.GetVirtualDesktopBounds(monitors);

        Assert.Equal(-1080, bounds.Left);
        Assert.Equal(0, bounds.Top);
        Assert.Equal(1920, bounds.Right);
        Assert.Equal(1920, bounds.Height);
    }
}
