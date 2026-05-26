using Moza.ScLink.App.GameLog;

namespace Moza.ScLink.App.Tests;

/// <summary>Configurable <see cref="IGameLogPathProvider"/> test double for the gutted MainViewModel.</summary>
internal sealed class StubGameLogPathProvider : IGameLogPathProvider
{
    public GameLogPathResolution StartupResolution { get; init; } = new(null, GameLogPathOrigin.None);

    public GameLogPathResolution AutoDetectResult { get; init; } =
        new("D:/detected/Game.log", GameLogPathOrigin.Detected);

    public string? LastExplicitPath { get; private set; }

    public GameLogPathResolution ResolveAtStartup() => StartupResolution;

    public GameLogPathResolution AutoDetect() => AutoDetectResult;

    public GameLogPathResolution UseExplicitPath(string path)
    {
        LastExplicitPath = path;
        return new GameLogPathResolution(path, GameLogPathOrigin.Explicit);
    }
}
