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
    /// DIP-&gt;physical mapping.
    /// </summary>
    private void ShowInitialRegion(Rectangle regionInVirtualDesktopCoords)
    {
        var clamped = Rectangle.Intersect(regionInVirtualDesktopCoords, _virtualDesktopBounds);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return; // Remembered region no longer fits the current desktop layout.
        }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                         ?? System.Windows.Media.Matrix.Identity;

        var topLeftDip = transform.Transform(new Point(
            clamped.Left - _virtualDesktopBounds.Left,
            clamped.Top - _virtualDesktopBounds.Top));
        var bottomRightDip = transform.Transform(new Point(
            clamped.Right - _virtualDesktopBounds.Left,
            clamped.Bottom - _virtualDesktopBounds.Top));

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

        // WPF hands us mouse positions in this window's device-independent units
        // (DIPs). Under Per-Monitor DPI Aware V2 (set in app.manifest), WPF keeps
        // each window's CompositionTarget DPI matrix in sync with whichever monitor
        // it's currently on, so converting through TransformToDevice here yields the
        // correct physical-pixel offset for *this* monitor's scale factor, even on
        // mixed-DPI setups. We then add the window's own top-left (which was set in
        // physical pixels via SetWindowPos) to get absolute virtual-desktop coordinates.
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
                         ?? System.Windows.Media.Matrix.Identity;
        var topLeftDevice = transform.Transform(new Point(x, y));
        var bottomRightDevice = transform.Transform(new Point(x + w, y + h));

        _selectedRegion = new Rectangle(
            _virtualDesktopBounds.Left + (int)Math.Round(topLeftDevice.X),
            _virtualDesktopBounds.Top + (int)Math.Round(topLeftDevice.Y),
            (int)Math.Round(bottomRightDevice.X - topLeftDevice.X),
            (int)Math.Round(bottomRightDevice.Y - topLeftDevice.Y));
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
