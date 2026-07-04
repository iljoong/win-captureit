using System.Threading;
using System.Windows;
using CaptureIt.App.Core;
using CaptureIt.App.Hotkeys;
using CaptureIt.App.Settings;
using CaptureIt.App.TrayIcon;

namespace CaptureIt.App;

/// <summary>
/// Application entry point. CaptureIt is tray-only (ShutdownMode=OnExplicitShutdown
/// in App.xaml, no StartupUri/main window). Enforces a single running instance via
/// a named mutex: a second launch signals the first instance to open Settings, then
/// exits immediately rather than registering a second (conflicting) global hotkey
/// and tray icon.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "CaptureIt.SingleInstance.Mutex";
    private const string ShowSettingsEventName = "CaptureIt.ShowSettings.Event";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsEvent;

    private SettingsService? _settingsService;
    private HotkeyManager? _hotkeyManager;
    private TrayIconManager? _trayIconManager;
    private CaptureController? _captureController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);

        if (!createdNew)
        {
            // Another instance is already running: ask it to open Settings, then exit.
            _showSettingsEvent.Set();
            Shutdown();
            return;
        }

        _settingsService = new SettingsService();
        _hotkeyManager = new HotkeyManager();
        _trayIconManager = new TrayIconManager();
        _captureController = new CaptureController(_settingsService, _trayIconManager);

        _trayIconManager.CaptureRegionRequested += (_, _) => _ = RunCaptureAsync(() => _captureController.CaptureRegion());
        _trayIconManager.CaptureFullScreenRequested += (_, _) => _ = RunCaptureAsync(() => _captureController.CaptureFullScreen());
        _trayIconManager.SettingsRequested += (_, _) => OpenSettings();
        _trayIconManager.ExitRequested += (_, _) => Shutdown();

        _hotkeyManager.HotkeyPressed += (_, _) => _ = RunCaptureAsync(() => _captureController.CaptureLastUsedMode());

        var initialSettings = _settingsService.Load();
        if (!_hotkeyManager.TryRegister(initialSettings.Hotkey))
        {
            _trayIconManager.ShowFailureNotification(
                "Hotkey unavailable",
                $"CaptureIt's hotkey ({initialSettings.Hotkey}) is already in use by another app. " +
                "Open Settings to choose a different one; you can still capture from the tray menu.");
        }

        // Listen for a second-instance launch requesting Settings be shown.
        RegisterWaitForShowSettingsSignal();
    }

    private void RegisterWaitForShowSettingsSignal()
    {
        if (_showSettingsEvent is null)
        {
            return;
        }

        ThreadPool.RegisterWaitForSingleObject(
            _showSettingsEvent,
            (_, _) => Dispatcher.Invoke(OpenSettings),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void OpenSettings()
    {
        if (_settingsService is null || _hotkeyManager is null)
        {
            return;
        }

        var window = new SettingsWindow(_settingsService, _hotkeyManager)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.Activate();
        window.ShowDialog();
    }

    private async Task RunCaptureAsync(Func<Task> captureAsync)
    {
        try
        {
            await captureAsync();
        }
        catch (Exception ex)
        {
            _trayIconManager?.ShowFailureNotification("Screenshot failed", ex.Message);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _trayIconManager?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
