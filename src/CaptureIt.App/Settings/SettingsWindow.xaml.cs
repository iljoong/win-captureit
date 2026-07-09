using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CaptureIt.App.Capture;
using CaptureIt.App.Hotkeys;
using CaptureIt.App.Models;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using WinFormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace CaptureIt.App.Settings;

/// <summary>
/// Settings window: save folder, filename pattern (with live preview), global
/// hotkey remapping, and AI capture configuration. Hotkey changes are
/// test-registered before saving so we never silently accept a combination
/// Windows or another app already owns.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly HotkeyManager _hotkeyManager;
    private AppSettings _workingCopy;
    private HotkeyDefinition _pendingHotkey;

    /// <summary>
    /// Working list of AI Task templates, kept in sync with <see cref="AiTaskComboBox"/>.
    /// Cloned from settings on load so Cancel discards any add/edit/delete.
    /// </summary>
    private List<AiTaskTemplate> _aiTasks = new();

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
        PopulateCaptureDelayChoices();
        PopulateSavingOptionChoices();
        PopulateAiCaptureSection();
        UpdateTextExtractOptionsEnabled();
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

    /// <summary>
    /// Surfaces the saving option as a fixed set of choices (save to file, clipboard,
    /// both, or off), replacing the earlier "Save to clipboard" checkbox.
    /// </summary>
    private void PopulateSavingOptionChoices()
    {
        SavingOptionComboBox.ItemsSource = new List<SavingChoice>
        {
            new("Save to file", SavingOption.SaveToFile),
            new("Save to clipboard", SavingOption.SaveToClipboard),
            new("Save to file and clipboard", SavingOption.SaveToFileAndClipboard),
            new("Off", SavingOption.Off),
        };
        SavingOptionComboBox.DisplayMemberPath = nameof(SavingChoice.Label);
        SavingOptionComboBox.SelectedValuePath = nameof(SavingChoice.Option);
        SavingOptionComboBox.SelectedValue = _workingCopy.Saving;
    }

    private sealed record SavingChoice(string Label, SavingOption Option);

    private sealed record AiModeChoice(string Label, AiCaptureMode Mode);

    private void OnOcrEnabledChanged(object sender, RoutedEventArgs e) => UpdateTextExtractOptionsEnabled();

    /// <summary>
    /// Enables the extraction-method dropdown and all AI fields only while "Extract
    /// text from captured screenshots" is checked; otherwise the whole sub-panel is
    /// greyed out (the master checkbox itself stays enabled).
    /// </summary>
    private void UpdateTextExtractOptionsEnabled()
    {
        if (TextExtractOptionsPanel is not null)
        {
            TextExtractOptionsPanel.IsEnabled = OcrEnabledCheckBox.IsChecked == true;
        }
    }

    /// <summary>
    /// Populates the AI capture mode dropdown, text fields, and MCP server rows from
    /// the working copy. The API key itself is never loaded into the UI (it lives in
    /// Windows Credential Manager) — only a hint about whether one is already saved.
    /// </summary>
    private void PopulateAiCaptureSection()
    {
        ExtractMethodComboBox.ItemsSource = new List<AiModeChoice>
        {
            new("use Windows OCR", AiCaptureMode.WindowsOcr),
            new("Use AI to capture", AiCaptureMode.Capture),
            new("Use AI to answer", AiCaptureMode.Answer),
        };
        ExtractMethodComboBox.DisplayMemberPath = nameof(AiModeChoice.Label);
        ExtractMethodComboBox.SelectedValuePath = nameof(AiModeChoice.Mode);
        ExtractMethodComboBox.SelectedValue = _workingCopy.AiCapture.Mode;

        AiBaseUrlTextBox.Text = _workingCopy.AiCapture.BaseUrl;
        AiModelTextBox.Text = _workingCopy.AiCapture.Model;

        AiApiKeyHintText.Text = CredentialManagerService.HasApiKey()
            ? "An API key is already saved. Leave blank to keep it, or enter a new value to replace it."
            : "No API key saved yet.";

        // Deep-clone so Cancel doesn't leave partial add/edit/delete changes behind in
        // the loaded settings object.
        _aiTasks = _workingCopy.AiCapture.AiTasks.Select(CloneAiTask).ToList();
        var selectedTask = _aiTasks.FirstOrDefault(t =>
            string.Equals(t.Name, _workingCopy.AiCapture.SelectedAiTaskName, StringComparison.OrdinalIgnoreCase));
        RefreshAiTaskComboBox(selectedTask ?? _aiTasks.FirstOrDefault());
    }

    private static AiTaskTemplate CloneAiTask(AiTaskTemplate task) => new()
    {
        Name = task.Name,
        Prompt = task.Prompt,
        UseWebSearch = task.UseWebSearch,
        McpServers = task.McpServers.Select(s => new McpServerEntry { Enabled = s.Enabled, Url = s.Url }).ToList(),
    };

    /// <summary>Rebinds <see cref="AiTaskComboBox"/> to the current <see cref="_aiTasks"/> list and selects <paramref name="select"/> (or the first item).</summary>
    private void RefreshAiTaskComboBox(AiTaskTemplate? select)
    {
        AiTaskComboBox.ItemsSource = null;
        AiTaskComboBox.ItemsSource = _aiTasks;
        AiTaskComboBox.DisplayMemberPath = nameof(AiTaskTemplate.Name);
        AiTaskComboBox.SelectedItem = select is not null && _aiTasks.Contains(select)
            ? select
            : _aiTasks.FirstOrDefault();

        UpdateAiTaskPromptText();
        UpdateAiTaskButtonsEnabled();
    }

    private void OnAiTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAiTaskPromptText();
        UpdateAiTaskButtonsEnabled();
    }

    private void UpdateAiTaskPromptText()
        => AiTaskPromptTextBox.Text = (AiTaskComboBox.SelectedItem as AiTaskTemplate)?.Prompt ?? string.Empty;

    /// <summary>Edit/delete only make sense when the list has at least one selected template.</summary>
    private void UpdateAiTaskButtonsEnabled()
    {
        bool hasSelection = AiTaskComboBox.SelectedItem is AiTaskTemplate;
        AiTaskEditButton.IsEnabled = hasSelection;
        AiTaskDeleteButton.IsEnabled = hasSelection;
    }

    private void OnAiTaskAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AiTaskDialog(template: null, otherTaskNames: _aiTasks.Select(t => t.Name).ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _aiTasks.Add(dialog.Result);
            RefreshAiTaskComboBox(dialog.Result);
        }
    }

    private void OnAiTaskEditClick(object sender, RoutedEventArgs e)
    {
        if (AiTaskComboBox.SelectedItem is not AiTaskTemplate selected)
        {
            return;
        }

        var otherNames = _aiTasks.Where(t => !ReferenceEquals(t, selected)).Select(t => t.Name).ToList();
        var dialog = new AiTaskDialog(selected, otherNames) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            var index = _aiTasks.IndexOf(selected);
            _aiTasks[index] = dialog.Result;
            RefreshAiTaskComboBox(dialog.Result);
        }
    }

    private void OnAiTaskDeleteClick(object sender, RoutedEventArgs e)
    {
        if (AiTaskComboBox.SelectedItem is not AiTaskTemplate selected)
        {
            return;
        }

        _aiTasks.Remove(selected);
        RefreshAiTaskComboBox(_aiTasks.FirstOrDefault());
    }

    /// <summary>Adds back any of the built-in AI Task templates whose name isn't already present.</summary>
    private void OnAiTaskRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        var existingNames = new HashSet<string>(_aiTasks.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var added = AiCaptureSettings.CreateDefaultAiTasks()
            .Where(t => !existingNames.Contains(t.Name))
            .ToList();

        if (added.Count == 0)
        {
            return;
        }

        _aiTasks.AddRange(added);
        RefreshAiTaskComboBox(added[0]);
    }

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
        _workingCopy.Saving = SavingOptionComboBox.SelectedValue is SavingOption saving ? saving : SavingOption.SaveToFile;

        _workingCopy.AiCapture.Mode = ExtractMethodComboBox.SelectedValue is AiCaptureMode mode ? mode : AiCaptureMode.WindowsOcr;
        _workingCopy.AiCapture.BaseUrl = string.IsNullOrWhiteSpace(AiBaseUrlTextBox.Text)
            ? AiCaptureSettings.DefaultBaseUrl
            : AiBaseUrlTextBox.Text.Trim();
        _workingCopy.AiCapture.Model = string.IsNullOrWhiteSpace(AiModelTextBox.Text)
            ? AiCaptureSettings.DefaultModel
            : AiModelTextBox.Text.Trim();
        _workingCopy.AiCapture.AiTasks = _aiTasks;
        _workingCopy.AiCapture.SelectedAiTaskName = (AiTaskComboBox.SelectedItem as AiTaskTemplate)?.Name;

        // Only touch Credential Manager if the user actually typed a new key; an
        // empty box means "keep whatever is already saved" rather than "clear it".
        if (!string.IsNullOrEmpty(AiApiKeyBox.Password))
        {
            try
            {
                CredentialManagerService.SaveApiKey(AiApiKeyBox.Password);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this,
                    $"The API key could not be saved to Windows Credential Manager: {ex.Message}",
                    "CaptureIt", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

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
