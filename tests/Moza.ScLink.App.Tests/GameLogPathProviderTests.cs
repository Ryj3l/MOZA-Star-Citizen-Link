using System.IO;
using Moza.ScLink.App.GameLog;
using Moza.ScLink.Profiles.Settings;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Exercises the §14.2-#3 path-resolution policy relocated from MainViewModel into
/// <see cref="GameLogPathProvider"/>. Detection is injected as a delegate and persistence uses a
/// temp-file-backed <see cref="AppSettingsStore"/> (internal test-path ctor), so every branch —
/// including "nothing detected" — runs hermetically regardless of whether Star Citizen is installed.
/// </summary>
public sealed class GameLogPathProviderTests : IDisposable
{
    private readonly string _tempDir;

    public GameLogPathProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "moza-sclink-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leaked temp dir does not fail the test run.
        }
    }

    private AppSettingsStore NewStore() => new(Path.Combine(_tempDir, "settings.json"));

    private string CreateLog(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "log");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void ResolveAtStartupOnCleanMachineReturnsNone()
    {
        var provider = new GameLogPathProvider(NewStore(), () => null);

        var result = provider.ResolveAtStartup();

        Assert.Equal(GameLogPathOrigin.None, result.Origin);
        Assert.Null(result.Path);
    }

    [Fact]
    public void ResolveAtStartupWithNoSavedButDetectedReturnsDetectedAndPersists()
    {
        var detected = CreateLog("Game.log", DateTime.UtcNow);
        var store = NewStore();
        var provider = new GameLogPathProvider(store, () => detected);

        var result = provider.ResolveAtStartup();

        Assert.Equal(GameLogPathOrigin.Detected, result.Origin);
        Assert.Equal(detected, result.Path);
        Assert.Equal(detected, store.Load().GameLogPath);
    }

    [Fact]
    public void ResolveAtStartupWithSavedAndDetectedSamePathReturnsSaved()
    {
        var saved = CreateLog("Game.log", DateTime.UtcNow);
        var store = NewStore();
        store.Save(new AppSettings { GameLogPath = saved });
        var provider = new GameLogPathProvider(store, () => saved);

        var result = provider.ResolveAtStartup();

        Assert.Equal(GameLogPathOrigin.Saved, result.Origin);
        Assert.Equal(saved, result.Path);
    }

    [Fact]
    public void ResolveAtStartupWhenDetectedNewerThanSavedReplacesAndPersists()
    {
        var saved = CreateLog("saved.log", DateTime.UtcNow.AddHours(-1));
        var detected = CreateLog("detected.log", DateTime.UtcNow);
        var store = NewStore();
        store.Save(new AppSettings { GameLogPath = saved });
        var provider = new GameLogPathProvider(store, () => detected);

        var result = provider.ResolveAtStartup();

        Assert.Equal(GameLogPathOrigin.ReplacedWithNewer, result.Origin);
        Assert.Equal(detected, result.Path);
        Assert.Equal(detected, store.Load().GameLogPath);
    }

    [Fact]
    public void ResolveAtStartupWhenDetectedNotNewerThanSavedReturnsSaved()
    {
        var saved = CreateLog("saved.log", DateTime.UtcNow);
        var detected = CreateLog("detected.log", DateTime.UtcNow.AddHours(-1));
        var store = NewStore();
        store.Save(new AppSettings { GameLogPath = saved });
        var provider = new GameLogPathProvider(store, () => detected);

        var result = provider.ResolveAtStartup();

        Assert.Equal(GameLogPathOrigin.Saved, result.Origin);
        Assert.Equal(saved, result.Path);
    }

    [Fact]
    public void ResolveAtStartupWithCorruptedSettingsRecoversAndUsesDetected()
    {
        // §14.2-#4: a corrupted settings file must not crash resolution. AppSettingsStore.Load recovers
        // to a default (empty) AppSettings, so the provider falls through to the detected log.
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{ this is not valid json");
        var detected = CreateLog("Game.log", DateTime.UtcNow);
        var provider = new GameLogPathProvider(NewStore(), () => detected);

        var result = provider.ResolveAtStartup();

        Assert.Equal(GameLogPathOrigin.Detected, result.Origin);
        Assert.Equal(detected, result.Path);
    }

    [Fact]
    public void AutoDetectWhenDetectedPersistsAndReturnsDetected()
    {
        var detected = CreateLog("Game.log", DateTime.UtcNow);
        var store = NewStore();
        var provider = new GameLogPathProvider(store, () => detected);

        var result = provider.AutoDetect();

        Assert.Equal(GameLogPathOrigin.Detected, result.Origin);
        Assert.Equal(detected, result.Path);
        Assert.Equal(detected, store.Load().GameLogPath);
    }

    [Fact]
    public void AutoDetectWithNothingDetectedReturnsNone()
    {
        var provider = new GameLogPathProvider(NewStore(), () => null);

        var result = provider.AutoDetect();

        Assert.Equal(GameLogPathOrigin.None, result.Origin);
        Assert.Null(result.Path);
    }
}
