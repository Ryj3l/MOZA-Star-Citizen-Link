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
}
