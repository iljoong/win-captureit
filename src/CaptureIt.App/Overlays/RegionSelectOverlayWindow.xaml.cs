using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaptureIt.App.Capture;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;

namespace CaptureIt.App.Overlays;

/// <summary>
/// Full-virtual-desktop overlay for region selection. Displays the already-frozen
/// desktop bitmap (captured before this window ever appears) so nothing this window
/// draws can end up "inside" the final screenshot. The window is positioned and
/// sized directly in physical pixels via SetWindowPos so it spans the exact virtual
/// desktop bounds regardless of per-monitor DPI scaling.
/// </summary>
public partial class RegionSelectOverlayWindow : Window
{
    private readonly Bitmap _frozenDesktop;
    private readonly Rectangle _virtualDesktopBounds;
    private readonly Rectangle? _initialRegion;

    private Point? _dragStart;
    private Rectangle? _selectedRegion;

    /// <summary>Result of the interaction: the selected region in virtual-desktop physical-pixel coordinates, or null if cancelled.</summary>
    public Rectangle? Result { get; private set; }

    public RegionSelectOverlayWindow(Bitmap frozenDesktop, Rectangle virtualDesktopBounds, Rectangle? initialRegion = null)
    {
        InitializeComponent();

        _frozenDesktop = frozenDesktop;
        _virtualDesktopBounds = virtualDesktopBounds;
        _initialRegion = initialRegion;

        FrozenDesktopImage.Source = ToBitmapSource(frozenDesktop);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Position/size this window in raw physical pixels so it exactly spans the
        // virtual desktop irrespective of WPF's DPI-dependent Left/Top/Width/Height
        // units. This sidesteps mixed-DPI multi-monitor coordinate bugs.
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeWindowPositioning.SetWindowBoundsPhysicalPixels(hwnd, _virtualDesktopBounds);

        // Grab foreground + keyboard focus so Enter/Esc work without a preceding click
        // (the app is a background tray app, so activation isn't automatic).
        NativeWindowPositioning.ForceForeground(hwnd);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // If we have a remembered region from a previous capture, pre-draw it so the
        // user can immediately confirm it with Enter (or drag a new one instead).
        if (_initialRegion is { } region)
        {
            ShowInitialRegion(region);
        }
    }

    /// <summary>
    /// Draws the given virtual-desktop region as the current selection and records it
    /// so Enter captures it without any drag. Inverse of <see cref="UpdateSelectionVisual"/>'s
    /// image-to-virtual-desktop mapping.
    /// </summary>
    private void ShowInitialRegion(Rectangle regionInVirtualDesktopCoords)
    {
        var clamped = Rectangle.Intersect(regionInVirtualDesktopCoords, _virtualDesktopBounds);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return; // Remembered region no longer fits the current desktop layout.
        }

        var imageSize = GetFrozenDesktopImageSize();
        var topLeftDip = VirtualDesktopToImagePoint(clamped.Left, clamped.Top, imageSize);
        var bottomRightDip = VirtualDesktopToImagePoint(clamped.Right, clamped.Bottom, imageSize);

        Canvas.SetLeft(SelectionRectangle, topLeftDip.X);
        Canvas.SetTop(SelectionRectangle, topLeftDip.Y);
        SelectionRectangle.Width = bottomRightDip.X - topLeftDip.X;
        SelectionRectangle.Height = bottomRightDip.Y - topLeftDip.Y;
        SelectionRectangle.Visibility = Visibility.Visible;

        _selectedRegion = clamped;
        HintText.Text = "Press Enter to reuse the last region, or drag to select a new one. Esc to cancel.";
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = null;
            Close();
        }
        else if (e.Key == Key.Enter && _selectedRegion is { Width: > 0, Height: > 0 })
        {
            Result = _selectedRegion;
            Close();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        SelectionRectangle.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        UpdateSelectionVisual(start, current);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        ReleaseMouseCapture();
        var end = e.GetPosition(this);
        UpdateSelectionVisual(start, end);

        if (_selectedRegion is { Width: > 2, Height: > 2 })
        {
            Result = _selectedRegion;
            Close();
        }
        else
        {
            // Treat a near-zero-size drag (an accidental click) as a no-op, not a cancel;
            // let the user try again instead of closing the overlay.
            SelectionRectangle.Visibility = Visibility.Collapsed;
            _dragStart = null;
            _selectedRegion = null;
        }
    }

    private void UpdateSelectionVisual(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var w = Math.Abs(end.X - start.X);
        var h = Math.Abs(end.Y - start.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = w;
        SelectionRectangle.Height = h;

        var imageSize = GetFrozenDesktopImageSize();
        var topLeft = ImagePointToVirtualDesktop(x, y, imageSize);
        var bottomRight = ImagePointToVirtualDesktop(x + w, y + h, imageSize);

        _selectedRegion = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    private Size GetFrozenDesktopImageSize()
    {
        var width = FrozenDesktopImage.ActualWidth > 0 ? FrozenDesktopImage.ActualWidth : ActualWidth;
        var height = FrozenDesktopImage.ActualHeight > 0 ? FrozenDesktopImage.ActualHeight : ActualHeight;

        if (width <= 0 || height <= 0)
        {
            return new Size(_virtualDesktopBounds.Width, _virtualDesktopBounds.Height);
        }

        return new Size(width, height);
    }

    private Point VirtualDesktopToImagePoint(int virtualX, int virtualY, Size imageSize)
    {
        var x = (virtualX - _virtualDesktopBounds.Left) * imageSize.Width / _virtualDesktopBounds.Width;
        var y = (virtualY - _virtualDesktopBounds.Top) * imageSize.Height / _virtualDesktopBounds.Height;
        return new Point(x, y);
    }

    private System.Drawing.Point ImagePointToVirtualDesktop(double imageX, double imageY, Size imageSize)
    {
        var clampedX = Math.Clamp(imageX, 0, imageSize.Width);
        var clampedY = Math.Clamp(imageY, 0, imageSize.Height);
        var virtualX = _virtualDesktopBounds.Left + (int)Math.Round(clampedX * _virtualDesktopBounds.Width / imageSize.Width);
        var virtualY = _virtualDesktopBounds.Top + (int)Math.Round(clampedY * _virtualDesktopBounds.Height / imageSize.Height);
        return new System.Drawing.Point(virtualX, virtualY);
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethodsGdi.DeleteObject(hBitmap);
        }
    }
}
