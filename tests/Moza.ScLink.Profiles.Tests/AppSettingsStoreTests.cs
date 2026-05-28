using System.IO;
using Moza.ScLink.Profiles.Settings;

namespace Moza.ScLink.Profiles.Tests;

/// <summary>
/// Covers PRP §14.2-#4 (AppSettingsStore corrupted-file recovery) — previously untested. Uses the
/// internal test-path ctor to point the store at a temp file so the recovery and round-trip behavior
/// are exercised hermetically.
/// </summary>
public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public AppSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "moza-sclink-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void LoadWithMissingFileReturnsDefault()
    {
        var store = new AppSettingsStore(_settingsPath);

        var settings = store.Load();

        Assert.NotNull(settings);
        Assert.Null(settings.GameLogPath);
    }

    [Fact]
    public void LoadWithCorruptedJsonReturnsDefaultWithoutThrowing()
    {
        File.WriteAllText(_settingsPath, "{ not: valid json ]");
        var store = new AppSettingsStore(_settingsPath);

        var settings = store.Load();

        Assert.NotNull(settings);
        Assert.Null(settings.GameLogPath);
    }

    [Fact]
    public void LoadWithEmptyFileReturnsDefaultWithoutThrowing()
    {
        File.WriteAllText(_settingsPath, string.Empty);
        var store = new AppSettingsStore(_settingsPath);

        var settings = store.Load();

        Assert.NotNull(settings);
        Assert.Null(settings.GameLogPath);
    }

    [Fact]
    public void SaveThenLoadRoundTripsGameLogPath()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings { GameLogPath = @"C:\Games\StarCitizen\LIVE\Game.log" });

        var settings = store.Load();

        Assert.Equal(@"C:\Games\StarCitizen\LIVE\Game.log", settings.GameLogPath);
    }

    [Fact]
    public void SaveThenLoadRoundTripsForcePreviewMode()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings { ForcePreviewMode = true });

        var settings = store.Load();

        Assert.True(settings.ForcePreviewMode);
    }

    // Regression guard for the T-27 latent bug: whole-object Save with a freshly constructed
    // AppSettings resets sibling fields. Update(Load-mutate-save) must preserve them. Without the
    // fix, mutating ForcePreviewMode would null out a previously saved GameLogPath (and vice versa).
    [Fact]
    public void UpdatePreservesSiblingFields()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings { GameLogPath = @"C:\Games\StarCitizen\LIVE\Game.log" });

        store.Update(s => s.ForcePreviewMode = true);

        var settings = store.Load();
        Assert.Equal(@"C:\Games\StarCitizen\LIVE\Game.log", settings.GameLogPath);
        Assert.True(settings.ForcePreviewMode);
    }

    [Fact]
    public void UpdateMutatingGameLogPathPreservesForcePreviewMode()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings { ForcePreviewMode = true });

        store.Update(s => s.GameLogPath = @"D:\SC\Game.log");

        var settings = store.Load();
        Assert.True(settings.ForcePreviewMode);
        Assert.Equal(@"D:\SC\Game.log", settings.GameLogPath);
    }

    [Fact]
    public void UpdateOnMissingFileStartsFromDefault()
    {
        var store = new AppSettingsStore(_settingsPath);

        store.Update(s => s.ForcePreviewMode = true);

        var settings = store.Load();
        Assert.True(settings.ForcePreviewMode);
        Assert.Null(settings.GameLogPath);
    }

    [Fact]
    public void UpdateWithNullMutatorThrows()
    {
        var store = new AppSettingsStore(_settingsPath);

        Assert.Throws<ArgumentNullException>(() => store.Update(null!));
    }

    [Fact]
    public void LoadWithMissingFileReturnsDefaultEmergencyStopHotkey()
    {
        var store = new AppSettingsStore(_settingsPath);

        var settings = store.Load();

        Assert.Equal("Ctrl+Alt+F12", settings.EmergencyStopHotkey);
    }

    [Fact]
    public void SaveThenLoadRoundTripsEmergencyStopHotkey()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings { EmergencyStopHotkey = "Ctrl+Shift+P" });

        var settings = store.Load();

        Assert.Equal("Ctrl+Shift+P", settings.EmergencyStopHotkey);
    }

    [Fact]
    public void UpdateMutatingForcePreviewModePreservesEmergencyStopHotkey()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings { EmergencyStopHotkey = "Alt+F9" });

        store.Update(s => s.ForcePreviewMode = true);

        var settings = store.Load();
        Assert.Equal("Alt+F9", settings.EmergencyStopHotkey);
        Assert.True(settings.ForcePreviewMode);
    }
}
