using System.Text.Json;
using CaptureIt.App.Models;
using CaptureIt.App.Settings;
using Xunit;

namespace CaptureIt.Tests;

/// <summary>
/// Tests SettingsService's JSON persistence and, importantly, its recovery-to-defaults
/// behavior when the settings file is missing or corrupted — a tray-only background
/// app must never fail to start because of a bad settings file.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CaptureItTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private SettingsService CreateServiceWithFile(string? existingJson = null)
    {
        var service = new SettingsService(_tempDir);
        if (existingJson is not null)
        {
            File.WriteAllText(service.SettingsFilePath, existingJson);
        }
        return service;
    }

    [Fact]
    public void Load_WhenFileMissing_CreatesDefaultsAndPersistsThem()
    {
        var service = CreateServiceWithFile();

        var settings = service.Load();

        Assert.Equal(CaptureMode.Region, settings.LastCaptureMode);
        Assert.True(File.Exists(service.SettingsFilePath));
    }

    [Fact]
    public void Load_WhenFileCorrupted_RecoversWithDefaults()
    {
        var service = CreateServiceWithFile("{ not valid json ][");

        var settings = service.Load();

        Assert.NotNull(settings);
        Assert.False(string.IsNullOrWhiteSpace(settings.SaveFolder));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var service = CreateServiceWithFile();
        var settings = service.Load();
        settings.LastCaptureMode = CaptureMode.FullScreen;
        settings.FilenamePattern = "Custom_{date}";
        settings.SaveFolder = _tempDir;

        service.Save(settings);
        var reloaded = service.Load();

        Assert.Equal(CaptureMode.FullScreen, reloaded.LastCaptureMode);
        Assert.Equal("Custom_{date}", reloaded.FilenamePattern);
        Assert.Equal(_tempDir, reloaded.SaveFolder);
    }

    [Fact]
    public void Load_WhenSaveFolderIsBlank_FallsBackToDefault()
    {
        var json = JsonSerializer.Serialize(new { SaveFolder = "", FilenamePattern = "x", LastCaptureMode = 0, Hotkey = new { Modifiers = 2, VirtualKey = 0x53 } });
        var service = CreateServiceWithFile(json);

        var settings = service.Load();

        Assert.False(string.IsNullOrWhiteSpace(settings.SaveFolder));
    }

    [Fact]
    public void Load_WhenCaptureDelayUnsupported_NormalizesToImmediate()
    {
        var json = JsonSerializer.Serialize(new
        {
            SaveFolder = _tempDir,
            FilenamePattern = "x",
            LastCaptureMode = 0,
            CaptureDelaySeconds = 7,
            Hotkey = new { Modifiers = 2, VirtualKey = 0x53 }
        });
        var service = CreateServiceWithFile(json);

        var settings = service.Load();

        Assert.Equal(0, settings.CaptureDelaySeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void SaveThenLoad_PreservesSupportedCaptureDelay(int delay)
    {
        var service = CreateServiceWithFile();
        var settings = service.Load();
        settings.SaveFolder = _tempDir;
        settings.CaptureDelaySeconds = delay;

        service.Save(settings);
        var reloaded = service.Load();

        Assert.Equal(delay, reloaded.CaptureDelaySeconds);
    }

    [Fact]
    public void NewSettings_DefaultCaptureDelayIsImmediate()
    {
        Assert.Equal(0, new AppSettings().CaptureDelaySeconds);
    }

    [Fact]
    public void NewSettings_SavingDefaultsToSaveToFile()
    {
        Assert.Equal(SavingOption.SaveToFile, new AppSettings().Saving);
    }

    [Theory]
    [InlineData(SavingOption.SaveToFile)]
    [InlineData(SavingOption.SaveToClipboard)]
    [InlineData(SavingOption.SaveToFileAndClipboard)]
    [InlineData(SavingOption.Off)]
    public void SaveThenLoad_PreservesSavingOption(SavingOption saving)
    {
        var service = CreateServiceWithFile();
        var settings = service.Load();
        settings.SaveFolder = _tempDir;
        settings.Saving = saving;

        service.Save(settings);
        var reloaded = service.Load();

        Assert.Equal(saving, reloaded.Saving);
    }
}
