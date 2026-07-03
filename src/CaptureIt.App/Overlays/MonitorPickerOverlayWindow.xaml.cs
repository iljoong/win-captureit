using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureIt.App.Models;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;

namespace CaptureIt.App.Overlays;

/// <summary>
/// Shows a numbered overlay on each monitor so the user can pick which one to
/// capture full-screen. Only shown when there is more than one monitor; the caller
/// should skip straight to capture when a single monitor is present. Renders over
/// the already-frozen desktop bitmap, per the freeze-first capture model.
/// </summary>
public partial class MonitorPickerOverlayWindow : Window
{
    private readonly Bitmap _frozenDesktop;
    private readonly Rectangle _virtualDesktopBounds;
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly string? _lastMonitorDeviceName;

    /// <summary>The monitor the user picked, or null if cancelled.</summary>
    public MonitorInfo? Result { get; private set; }

    public MonitorPickerOverlayWindow(Bitmap frozenDesktop, Rectangle virtualDesktopBounds, IReadOnlyList<MonitorInfo> monitors, string? lastMonitorDeviceName = null)
    {
        InitializeComponent();

        _frozenDesktop = frozenDesktop;
        _virtualDesktopBounds = virtualDesktopBounds;
        _monitors = monitors;
        _lastMonitorDeviceName = lastMonitorDeviceName;

        FrozenDesktopImage.Source = ToBitmapSource(frozenDesktop);

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => BuildMonitorOverlays();
        KeyDown += OnKeyDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeWindowPositioning.SetWindowBoundsPhysicalPixels(hwnd, _virtualDesktopBounds);

        // Grab foreground + keyboard focus so Esc/Enter/number-key shortcuts work
        // without a preceding click (background tray app: activation isn't automatic).
        NativeWindowPositioning.ForceForeground(hwnd);
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void BuildMonitorOverlays()
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                         ?? Matrix.Identity;

        foreach (var monitor in _monitors)
        {
            bool isLastUsed = _lastMonitorDeviceName is not null
                && string.Equals(monitor.DeviceName, _lastMonitorDeviceName, StringComparison.OrdinalIgnoreCase);

            // Convert this monitor's physical-pixel bounds (relative to the virtual
            // desktop origin) into this window's local DIP coordinates.
            var localPhysicalTopLeft = new Point(
                monitor.Bounds.Left - _virtualDesktopBounds.Left,
                monitor.Bounds.Top - _virtualDesktopBounds.Top);
            var localPhysicalBottomRight = new Point(
                localPhysicalTopLeft.X + monitor.Bounds.Width,
                localPhysicalTopLeft.Y + monitor.Bounds.Height);

            var topLeftDip = transform.Transform(localPhysicalTopLeft);
            var bottomRightDip = transform.Transform(localPhysicalBottomRight);

            // Pre-highlight the monitor used in the last full-screen capture so the
            // user can confirm it with Enter (brighter fill, gold border).
            var border = new Border
            {
                Width = bottomRightDip.X - topLeftDip.X,
                Height = bottomRightDip.Y - topLeftDip.Y,
                Background = new SolidColorBrush(isLastUsed
                    ? System.Windows.Media.Color.FromArgb(0x44, 0, 0, 0)
                    : System.Windows.Media.Color.FromArgb(0x88, 0, 0, 0)),
                BorderBrush = isLastUsed
                    ? System.Windows.Media.Brushes.Gold
                    : System.Windows.Media.Brushes.DeepSkyBlue,
                BorderThickness = new Thickness(isLastUsed ? 5 : 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var label = new TextBlock
            {
                Text = isLastUsed ? $"{monitor.Index}  (last)" : monitor.Index.ToString(),
                FontSize = 96,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            border.Child = label;

            border.MouseLeftButtonUp += (_, _) => SelectMonitor(monitor);

            Canvas.SetLeft(border, topLeftDip.X);
            Canvas.SetTop(border, topLeftDip.Y);
            MonitorOverlaysCanvas.Children.Add(border);
        }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = null;
            Close();
            return;
        }

        // Enter re-captures the monitor used last time (if it still exists).
        if (e.Key == Key.Enter && _lastMonitorDeviceName is not null)
        {
            var lastUsed = _monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, _lastMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (lastUsed is not null)
            {
                SelectMonitor(lastUsed);
            }
            return;
        }

        // Support 1-9 number-key shortcuts matching the on-screen labels.
        var numberPressed = KeyToDigit(e.Key);
        if (numberPressed is { } digit)
        {
            var match = _monitors.FirstOrDefault(m => m.Index == digit);
            if (match is not null)
            {
                SelectMonitor(match);
            }
        }
    }

    private static int? KeyToDigit(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null
    };

    private void SelectMonitor(MonitorInfo monitor)
    {
        Result = monitor;
        Close();
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
