using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CaptureIt.App.TrayIcon;

/// <summary>
/// Owns the tray (notification area) icon and its right-click context menu:
/// Capture Region, Capture Full Screen, Settings, Exit. Recreates the icon if
/// Explorer restarts (WM_TASKBARCREATED), so the app doesn't silently vanish from
/// the tray.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TaskbarCreatedListener _taskbarCreatedListener;
    private readonly Icon _trayIcon;

    public event EventHandler? CaptureRegionRequested;
    public event EventHandler? CaptureFullScreenRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIconManager()
    {
        _trayIcon = LoadTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "CaptureIt",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        // Explorer.exe can crash/restart, which silently destroys tray icons. Listen
        // for the broadcast "TaskbarCreated" message and re-show ours when it fires.
        _taskbarCreatedListener = new TaskbarCreatedListener(() => _notifyIcon.Visible = true);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var captureRegionItem = new ToolStripMenuItem("Capture Region");
        captureRegionItem.Click += (_, _) => CaptureRegionRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(captureRegionItem);

        var captureFullScreenItem = new ToolStripMenuItem("Capture Full Screen");
        captureFullScreenItem.Click += (_, _) => CaptureFullScreenRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(captureFullScreenItem);

        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>
    /// Loads the multi-resolution tray icon from the embedded <c>Assets\capture.ico</c>
    /// resource (packed via a pack:// URI). NotifyIcon picks the size that best matches
    /// the current DPI from the sizes contained in the .ico.
    /// </summary>
    private static Icon LoadTrayIcon()
    {
        var uri = new Uri("pack://application:,,,/Assets/capture.ico", UriKind.Absolute);
        using var stream = System.Windows.Application.GetResourceStream(uri).Stream;
        return new Icon(stream);
    }

    /// <summary>Shows a Windows notification balloon. Used only for failure cases per the app's UX spec (silent on success).</summary>
    public void ShowFailureNotification(string title, string message)
    {
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(6000);
    }

    public void Dispose()
    {
        _taskbarCreatedListener.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
    }
}

/// <summary>
/// Minimal hidden window that listens for the "TaskbarCreated" broadcast message
/// (sent by Explorer after it restarts) so the tray icon can be re-shown.
/// </summary>
internal sealed class TaskbarCreatedListener : NativeWindow, IDisposable
{
    private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
    private readonly Action _onTaskbarCreated;

    public TaskbarCreatedListener(Action onTaskbarCreated)
    {
        _onTaskbarCreated = onTaskbarCreated;
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_TASKBARCREATED)
        {
            _onTaskbarCreated();
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string lpString);

    public void Dispose() => DestroyHandle();
}
