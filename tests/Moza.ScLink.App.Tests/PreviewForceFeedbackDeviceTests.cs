using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moza.ScLink.App.ForceFeedback;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Models;

// Disambiguate from the legacy Moza.ScLink.Core.Models.ForceEffect — the canonical effect descriptor
// carried by PlayEffectCommand lives in Core.Effects.
using ForceEffect = Moza.ScLink.Core.Effects.ForceEffect;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Covers <see cref="PreviewForceFeedbackDevice"/> (T-17). The four spec-named tests map to PascalCase
/// (CA1707): <c>Logs_Each_Command</c> → <see cref="LogsEachCommand"/>, <c>Reports_Active_Count_Correctly</c>
/// → <see cref="ReportsActiveCountCorrectly"/>, <c>StopAll_Clears_Active_Effects</c> →
/// <see cref="StopAllClearsActiveEffects"/>, <c>Stops_Effect_By_Id</c> → <see cref="StopsEffectById"/>.
/// Canonical-contract cases (state lifecycle, null-guard, capabilities) are migrated from the deleted
/// <c>LoggingNullForceFeedbackDeviceTests</c>.
/// </summary>
public sealed class PreviewForceFeedbackDeviceTests
{
    private static PreviewForceFeedbackDevice CreateDevice() =>
        new(NullLogger<PreviewForceFeedbackDevice>.Instance);

    private static PlayEffectCommand Play(string? stateKey, string effectId = "effect", double intensity = 0.5) =>
        new(
            new ForceEffect
            {
                EffectId = effectId,
                EffectType = ForceEffectType.Periodic,
                Category = EffectCategory.Combat,
                FrequencyHz = 30,
                Duration = TimeSpan.FromMilliseconds(250),
                DirectionX = 0.3,
                DirectionY = -0.4,
                IsSustained = stateKey is not null,
                StateKey = stateKey,
            },
            intensity)
        { CommandId = $"play-{effectId}", IssuedAt = DateTimeOffset.UnixEpoch };

    private static StopEffectCommand Stop(string stateKey) =>
        new(stateKey) { CommandId = $"stop-{stateKey}", IssuedAt = DateTimeOffset.UnixEpoch };

    private static StopAllCommand StopAll() =>
        new() { CommandId = "stop-all", IssuedAt = DateTimeOffset.UnixEpoch };

    // Collects every PreviewedCommand published to the device's Commands stream.
    private static (PreviewForceFeedbackDevice Device, List<PreviewedCommand> Captured, IDisposable Subscription) CreateSubscribedDevice()
    {
        var device = CreateDevice();
        var captured = new List<PreviewedCommand>();
        var subscription = device.Commands.Subscribe(new CapturingObserver(captured));
        return (device, captured, subscription);
    }

    // ── Spec-named tests ────────────────────────────────────────────────────────────────────────

    /// <summary>Spec: <c>Logs_Each_Command</c>. Every command produces exactly one log entry and one stream item.</summary>
    [Fact]
    public async Task LogsEachCommand()
    {
        var logger = new RecordingLogger<PreviewForceFeedbackDevice>();
        await using var device = new PreviewForceFeedbackDevice(logger);
        var captured = new List<PreviewedCommand>();
        using var _ = device.Commands.Subscribe(new CapturingObserver(captured));

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);
        await device.ExecuteAsync(Stop("k1"), CancellationToken.None);
        await device.ExecuteAsync(StopAll(), CancellationToken.None);

        // One Play / Stop / StopAll log entry (event ids 2/3/4); the init Information entry (id 1) is separate.
        Assert.Contains(logger.Entries, e => e.EventId.Id == 2 && e.Message.Contains("Preview Play"));
        Assert.Contains(logger.Entries, e => e.EventId.Id == 3 && e.Message.Contains("Preview Stop"));
        Assert.Contains(logger.Entries, e => e.EventId.Id == 4 && e.Message.Contains("Preview StopAll"));
        // One stream item per command.
        Assert.Equal(3, captured.Count);
        Assert.Equal(nameof(PlayEffectCommand), captured[0].CommandType);
        Assert.Equal(nameof(StopEffectCommand), captured[1].CommandType);
        Assert.Equal(nameof(StopAllCommand), captured[2].CommandType);
    }

    /// <summary>Spec: <c>Reports_Active_Count_Correctly</c>. Active count tracks sustained add/replace/stop/clear.</summary>
    [Fact]
    public async Task ReportsActiveCountCorrectly()
    {
        var (device, captured, subscription) = CreateSubscribedDevice();
        await using var _ = device;
        using var __ = subscription;

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);      // +k1 → 1
        await device.ExecuteAsync(Play("k2"), CancellationToken.None);      // +k2 → 2
        await device.ExecuteAsync(Play("k1"), CancellationToken.None);      // replace k1 → 2
        await device.ExecuteAsync(Play(stateKey: null), CancellationToken.None); // non-sustained → 2
        await device.ExecuteAsync(Stop("k2"), CancellationToken.None);      // -k2 → 1
        await device.ExecuteAsync(StopAll(), CancellationToken.None);       // clear → 0

        int[] expectedCounts = [1, 2, 2, 2, 1, 0];
        Assert.Equal(expectedCounts, captured.Select(c => c.ActiveCount));
    }

    /// <summary>Spec: <c>StopAll_Clears_Active_Effects</c>. StopAllCommand and StopAllAsync both clear the set.</summary>
    [Fact]
    public async Task StopAllClearsActiveEffects()
    {
        var (device, captured, subscription) = CreateSubscribedDevice();
        await using var _ = device;
        using var __ = subscription;

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);
        await device.ExecuteAsync(Play("k2"), CancellationToken.None);

        await device.ExecuteAsync(StopAll(), CancellationToken.None);
        Assert.Equal(0, captured[^1].ActiveCount);

        // Re-fill, then clear via the emergency-stop method path.
        await device.ExecuteAsync(Play("k3"), CancellationToken.None);
        Assert.Equal(1, captured[^1].ActiveCount);
        await device.StopAllAsync(CancellationToken.None);
        Assert.Equal(0, captured[^1].ActiveCount);
        Assert.Equal(nameof(PreviewForceFeedbackDevice.StopAllAsync), captured[^1].CommandType);
    }

    /// <summary>Spec: <c>Stops_Effect_By_Id</c>. StopEffectCommand removes by StateKey; an unknown key is a no-op.</summary>
    [Fact]
    public async Task StopsEffectById()
    {
        var (device, captured, subscription) = CreateSubscribedDevice();
        await using var _ = device;
        using var __ = subscription;

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);
        await device.ExecuteAsync(Play("k2"), CancellationToken.None);

        await device.ExecuteAsync(Stop("k1"), CancellationToken.None);
        Assert.Equal(1, captured[^1].ActiveCount);

        await device.ExecuteAsync(Stop("does-not-exist"), CancellationToken.None);
        Assert.Equal(1, captured[^1].ActiveCount); // unknown StateKey is a no-op

        await device.ExecuteAsync(Stop("k2"), CancellationToken.None);
        Assert.Equal(0, captured[^1].ActiveCount);
    }

    // ── Projection detail ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlayProjectionCarriesEffectDetail()
    {
        var (device, captured, subscription) = CreateSubscribedDevice();
        await using var _ = device;
        using var __ = subscription;

        await device.ExecuteAsync(Play("k1", effectId: "quantum-spool-v1", intensity: 0.75), CancellationToken.None);

        var p = Assert.Single(captured);
        Assert.Equal("quantum-spool-v1", p.EffectId);
        Assert.Equal(0.75, p.FinalIntensity);
        Assert.Equal(30, p.FrequencyHz);
        Assert.Equal(TimeSpan.FromMilliseconds(250), p.Duration);
        Assert.Equal(0.3, p.DirectionX);
        Assert.Equal(-0.4, p.DirectionY);
        Assert.Equal(DateTimeOffset.UnixEpoch, p.WallClock); // clock-free: reuses command.IssuedAt
    }

    [Fact]
    public async Task NonSustainedPlayDoesNotIncrementActiveCount()
    {
        var (device, captured, subscription) = CreateSubscribedDevice();
        await using var _ = device;
        using var __ = subscription;

        await device.ExecuteAsync(Play(stateKey: null, effectId: "one-shot"), CancellationToken.None);

        var p = Assert.Single(captured);
        Assert.Equal(0, p.ActiveCount);            // null StateKey → not tracked
        Assert.Equal("one-shot", p.EffectId);      // but still logged/published with full detail
        Assert.Equal(nameof(PlayEffectCommand), p.CommandType);
    }

    [Fact]
    public async Task StopProjectionHasNullEffectFields()
    {
        var (device, captured, subscription) = CreateSubscribedDevice();
        await using var _ = device;
        using var __ = subscription;

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);
        await device.ExecuteAsync(Stop("k1"), CancellationToken.None);

        var stop = captured[^1];
        Assert.Null(stop.EffectId);
        Assert.Null(stop.FinalIntensity);
        Assert.Null(stop.FrequencyHz);
        Assert.Null(stop.Duration);
        Assert.Null(stop.DirectionX);
        Assert.Null(stop.DirectionY);
    }

    // ── Subject subscribe / publish / unsubscribe ─────────────────────────────────────────────────

    [Fact]
    public async Task SubscriberStopsReceivingAfterDispose()
    {
        await using var device = CreateDevice();
        var captured = new List<PreviewedCommand>();
        var subscription = device.Commands.Subscribe(new CapturingObserver(captured));

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);
        Assert.Single(captured);

        subscription.Dispose();
        await device.ExecuteAsync(Play("k2"), CancellationToken.None);
        Assert.Single(captured); // no further items after unsubscribe
    }

    [Fact]
    public async Task MultipleSubscribersEachReceiveEveryCommand()
    {
        await using var device = CreateDevice();
        var a = new List<PreviewedCommand>();
        var b = new List<PreviewedCommand>();
        using var subA = device.Commands.Subscribe(new CapturingObserver(a));
        using var subB = device.Commands.Subscribe(new CapturingObserver(b));

        await device.ExecuteAsync(Play("k1"), CancellationToken.None);

        Assert.Single(a);
        Assert.Single(b);
    }

    [Fact]
    public async Task UnsubscribingOneSubscriberLeavesOthersActive()
    {
        await using var device = CreateDevice();
        var a = new List<PreviewedCommand>();
        var b = new List<PreviewedCommand>();
        var subA = device.Commands.Subscribe(new CapturingObserver(a));
        using var subB = device.Commands.Subscribe(new CapturingObserver(b));

        subA.Dispose();
        await device.ExecuteAsync(Play("k1"), CancellationToken.None);

        Assert.Empty(a);
        Assert.Single(b);
    }

    [Fact]
    public void DoubleDisposeOfSubscriptionIsSafe()
    {
        var device = CreateDevice();
        var sink = new List<PreviewedCommand>();
        var subscription = device.Commands.Subscribe(new CapturingObserver(sink));

        subscription.Dispose();
        subscription.Dispose(); // idempotent — must not throw
    }

    // ── Migrated canonical-contract cases (from LoggingNullForceFeedbackDeviceTests) ──────────────

    [Fact]
    public void StateIsDisconnectedBeforeInitialize()
    {
        var device = CreateDevice();
        Assert.Equal(DeviceState.Disconnected, device.State);
    }

    [Fact]
    public async Task InitializeRaisesRealDisconnectedToReadyTransition()
    {
        await using var device = CreateDevice();
        DeviceStateChangedEventArgs? observed = null;
        device.StateChanged += (_, e) => observed = e;

        await device.InitializeAsync(CancellationToken.None);

        Assert.Equal(DeviceState.Ready, device.State);
        Assert.NotNull(observed);
        Assert.Equal(DeviceState.Disconnected, observed!.Previous);
        Assert.Equal(DeviceState.Ready, observed.Current);
    }

    [Fact]
    public async Task ExecuteAsyncThrowsOnNullCommand()
    {
        await using var device = CreateDevice();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => device.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task StopAllAsyncCompletes()
    {
        await using var device = CreateDevice();
        await device.InitializeAsync(CancellationToken.None);

        await device.StopAllAsync(CancellationToken.None);
    }

    [Fact]
    public void ReportsUnknownModelAndPlaceholderCapabilities()
    {
        var device = CreateDevice();

        Assert.Equal(DeviceModel.Unknown, device.Model);
        Assert.Equal(DeviceModel.Unknown, device.Capabilities.Model);
        Assert.Equal(0, device.Capabilities.AxisCount);
        Assert.Equal(Guid.Empty, device.InstanceGuid);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────────────────────────

    private sealed class CapturingObserver(List<PreviewedCommand> sink) : IObserver<PreviewedCommand>
    {
        public void OnNext(PreviewedCommand value) => sink.Add(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<Entry> _entries = [];

        public IReadOnlyList<Entry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true; // capture Debug-level entries too

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _entries.Add(new Entry(logLevel, eventId, formatter(state, exception)));

        public sealed record Entry(LogLevel Level, EventId EventId, string Message);
    }
}
