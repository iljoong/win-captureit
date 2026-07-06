using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CaptureIt.App.Capture;
using CaptureIt.App.Hotkeys;
using CaptureIt.App.Models;
using WinFormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace CaptureIt.App.Settings;

/// <summary>
/// Settings window: save folder, filename pattern (with live preview), and global
/// hotkey remapping. Hotkey changes are test-registered before saving so we never
/// silently accept a combination Windows or another app already owns.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly HotkeyManager _hotkeyManager;
    private AppSettings _workingCopy;
    private HotkeyDefinition _pendingHotkey;

    public SettingsWindow(SettingsService settingsService, HotkeyManager hotkeyManager)
    {
        InitializeComponent();

        _settingsService = settingsService;
        _hotkeyManager = hotkeyManager;
        _workingCopy = _settingsService.Load();
        _pendingHotkey = _workingCopy.Hotkey;

        SaveFolderTextBox.Text = _workingCopy.SaveFolder;
        FilenamePatternTextBox.Text = _workingCopy.FilenamePattern;
        HotkeyTextBox.Text = _pendingHotkey.ToString();
        OcrEnabledCheckBox.IsChecked = _workingCopy.OcrEnabled;
        SaveToClipboardCheckBox.IsChecked = _workingCopy.SaveToClipboard;
        PopulateCaptureDelayChoices();
        UpdateFilenamePreview();
    }

    /// <summary>
    /// Surfaces the capture delay as a fixed set of choices (Off, 3s, 5s, 10s) rather
    /// than free-form input, so only supported values can ever be selected/saved.
    /// </summary>
    private void PopulateCaptureDelayChoices()
    {
        CaptureDelayComboBox.ItemsSource = AppSettings.SupportedCaptureDelays
            .Select(seconds => new DelayChoice(
                seconds == 0 ? "Off" : $"{seconds} seconds", seconds))
            .ToList();
        CaptureDelayComboBox.DisplayMemberPath = nameof(DelayChoice.Label);
        CaptureDelayComboBox.SelectedValuePath = nameof(DelayChoice.Seconds);
        CaptureDelayComboBox.SelectedValue = AppSettings.NormalizeCaptureDelay(_workingCopy.CaptureDelaySeconds);
    }

    private sealed record DelayChoice(string Label, int Seconds);

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinFormsFolderBrowserDialog
        {
            Description = "Choose a folder to save screenshots to",
            SelectedPath = SaveFolderTextBox.Text,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SaveFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void OnFilenamePatternTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateFilenamePreview();

    private void UpdateFilenamePreview()
    {
        var pattern = string.IsNullOrWhiteSpace(FilenamePatternTextBox.Text)
            ? "Screenshot_{datetime}"
            : FilenamePatternTextBox.Text;

        var preview = ImageSaveService.BuildFileName(pattern, DateTime.Now);
        FilenamePreviewText.Text = $"Preview: {preview}.png";
    }

    private void OnHotkeyPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Require at least one modifier so the hotkey doesn't hijack a plain key
        // used everywhere else in Windows.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        int modifierFlags = 0;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) modifierFlags |= ModifierFlags.Control;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) modifierFlags |= ModifierFlags.Alt;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) modifierFlags |= ModifierFlags.Shift;
        if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) modifierFlags |= ModifierFlags.Win;

        if (modifierFlags == 0)
        {
            HotkeyValidationText.Text = "Choose a combination that includes Ctrl, Alt, Shift, or Win.";
            return;
        }

        var candidate = new HotkeyDefinition
        {
            Modifiers = modifierFlags,
            VirtualKey = KeyInterop.VirtualKeyFromKey(key)
        };

        // Test-register on a throwaway probe window before accepting, so we can
        // reject combinations already reserved by Windows or another application
        // without disturbing the currently-active hotkey.
        var probeWindow = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Visibility = Visibility.Hidden };
        probeWindow.Show();
        probeWindow.Hide();
        var probeSource = (HwndSource)PresentationSource.FromVisual(probeWindow)!;

        bool canRegister = HotkeyManager.CanRegister(probeSource, candidate);
        probeSource.Dispose();
        probeWindow.Close();

        if (!canRegister)
        {
            HotkeyValidationText.Text = "That combination is already in use by Windows or another app. Try a different one.";
            return;
        }

        HotkeyValidationText.Text = string.Empty;
        _pendingHotkey = candidate;
        HotkeyTextBox.Text = candidate.ToString();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _workingCopy.SaveFolder = string.IsNullOrWhiteSpace(SaveFolderTextBox.Text)
            ? AppSettings.DefaultSaveFolder
            : SaveFolderTextBox.Text;
        _workingCopy.FilenamePattern = string.IsNullOrWhiteSpace(FilenamePatternTextBox.Text)
            ? "Screenshot_{datetime}"
            : FilenamePatternTextBox.Text;
        _workingCopy.Hotkey = _pendingHotkey;
        _workingCopy.CaptureDelaySeconds = AppSettings.NormalizeCaptureDelay(
            CaptureDelayComboBox.SelectedValue is int seconds ? seconds : 0);
        _workingCopy.OcrEnabled = OcrEnabledCheckBox.IsChecked == true;
        _workingCopy.SaveToClipboard = SaveToClipboardCheckBox.IsChecked == true;

        _settingsService.Save(_workingCopy);

        // Re-register the live hotkey with the newly-saved combination.
        if (!_hotkeyManager.TryRegister(_workingCopy.Hotkey))
        {
            HotkeyValidationText.Text = "Saved, but the hotkey could not be re-registered. It may have just been claimed by another app.";
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
