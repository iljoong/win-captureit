using System.Windows;
using System.Windows.Interop;
using CaptureIt.App.Capture;
using CaptureIt.App.Models;

namespace CaptureIt.App.Hotkeys;

/// <summary>
/// Registers a single global hotkey using RegisterHotKey/UnregisterHotKey via a
/// hidden message-only window, and raises <see cref="HotkeyPressed"/> when it fires.
/// Deliberately avoids a low-level keyboard hook (simpler, lower AV suspicion, and
/// sufficient for a single modifier+key combo).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xCA92; // Arbitrary app-specific id.

    private readonly Window _messageWindow;
    private readonly HwndSource _hwndSource;
    private bool _isRegistered;

    public event EventHandler? HotkeyPressed;

    public HotkeyManager()
    {
        // A zero-size, never-shown window whose sole purpose is to host an HWND that
        // can receive WM_HOTKEY messages.
        _messageWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden
        };
        _messageWindow.Show();
        _messageWindow.Hide();

        _hwndSource = (HwndSource)PresentationSource.FromVisual(_messageWindow)!;
        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// Attempts to register the given hotkey. Returns true on success. If a hotkey
    /// was already registered by this manager, it is unregistered first.
    /// </summary>
    public bool TryRegister(HotkeyDefinition hotkey)
    {
        Unregister();

        _isRegistered = NativeMethods.RegisterHotKey(
            _hwndSource.Handle, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey);

        return _isRegistered;
    }

    /// <summary>
    /// Validates that a hotkey combination can be registered right now, without
    /// leaving it registered. Used by the Settings UI to test a candidate hotkey
    /// before saving, so we never silently keep an old, working hotkey registered
    /// while telling the user their new one was accepted.
    /// </summary>
    public static bool CanRegister(HwndSource probeWindowSource, HotkeyDefinition hotkey)
    {
        const int probeId = 0xCA93;
        var ok = NativeMethods.RegisterHotKey(probeWindowSource.Handle, probeId, hotkey.Modifiers, hotkey.VirtualKey);
        if (ok)
        {
            NativeMethods.UnregisterHotKey(probeWindowSource.Handle, probeId);
        }
        return ok;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _hwndSource.RemoveHook(WndProc);
        _hwndSource.Dispose();
        _messageWindow.Close();
    }
}
