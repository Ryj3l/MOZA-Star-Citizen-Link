using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Logs.Parsing;

namespace Moza.ScLink.Logs;

/// <summary>
/// Game.log <see cref="ISensor"/>: tails the active Star Citizen Game.log (composing
/// <see cref="GameLogTailer"/> + a hot-reloadable <see cref="PatternLibrary"/>), classifies each line, and
/// publishes <see cref="SensorEvent"/>s to <see cref="IEventBus.SensorEvents"/> (PRP §2.7 producer) while
/// also exposing them via the §5.3 <see cref="ISensor.ReadEventsAsync"/> multicast.
/// </summary>
/// <remarks>
/// MIGRATION (T-11, tracked in #45): this sensor is the new bus-pipeline producer but is deliberately NOT
/// wired into the running app yet — the legacy MainViewModel → GameLogTailer → ForceFeedbackController path
/// remains the live runtime path (the bus has no consumer until fusion T-12 + resolver T-14). LogSensor is
/// exercised by unit tests and the manual sample-Game.log fixture test; it goes live when T-14 wires it
/// through DI, at which point the legacy direct path is removed (see #45).
/// </remarks>
public sealed class LogSensor : ISensor
{
    public const string LogSensorId = "log.game-log";

    private readonly IEventBus _bus;
    private readonly PatternLibrary _patternLibrary;
    private readonly string _gameLogPath;
    private readonly object _gate = new();

    private readonly ConcurrentDictionary<Guid, Channel<SensorEvent>> _subscribers = new();
    private GameLogTailer? _tailer;
    private SensorState _state = SensorState.Stopped;
    private long _eventsEmitted;
    private long _droppedEvents;
    private long _lastEventAtTicks;       // UTC ticks; 0 = never
    private volatile bool _isHealthy = true;
    private volatile string? _lastFault;
    private bool _disposed;

    public LogSensor(IEventBus bus, PatternLibrary patternLibrary, string gameLogPath)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(patternLibrary);
        ArgumentNullException.ThrowIfNull(gameLogPath);
        _bus = bus;
        _patternLibrary = patternLibrary;
        _gameLogPath = gameLogPath;
    }

    /// <inheritdoc />
    public string SensorId => LogSensorId;

    /// <inheritdoc />
    public SensorKind Kind => SensorKind.Log;

    /// <inheritdoc />
    public SensorState State
    {
        get { lock (_gate) { return _state; } }
    }

    /// <inheritdoc />
    public SensorHealth Health
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastEventAtTicks);
            return new SensorHealth(
                _isHealthy,
                _lastFault,
                Interlocked.Read(ref _eventsEmitted),
                Interlocked.Read(ref _droppedEvents),
                ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero));
        }
    }

    /// <inheritdoc />
    public event EventHandler<SensorHealthChangedEventArgs>? HealthChanged;

    /// <inheritdoc />
    public event EventHandler<SensorStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state is SensorState.Running or SensorState.Starting)
            {
                return Task.CompletedTask;  // idempotent
            }
        }

        TransitionTo(SensorState.Starting);
        try
        {
            var tailer = new GameLogTailer(_gameLogPath);
            tailer.LineRead += OnLineRead;
            tailer.Faulted += OnFaulted;
            tailer.Start(startAtEnd: true);
            lock (_gate) { _tailer = tailer; }
            TransitionTo(SensorState.Running);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            TransitionTo(SensorState.Faulted);
            throw new SensorStartException(SensorId, "Failed to start the log sensor.", ex);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        GameLogTailer? tailer;
        lock (_gate)
        {
            if (_state is SensorState.Stopped or SensorState.Stopping)
            {
                return;  // idempotent
            }

            tailer = _tailer;
            _tailer = null;
        }

        TransitionTo(SensorState.Stopping);
        if (tailer is not null)
        {
            tailer.LineRead -= OnLineRead;
            tailer.Faulted -= OnFaulted;
            var stopTask = tailer.StopAsync();
            var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
            if (completed != stopTask)
            {
                throw new TimeoutException("LogSensor stop exceeded the 5-second budget.");
            }

            await stopTask.ConfigureAwait(false);  // observe worker exceptions
            tailer.Dispose();
        }

        TransitionTo(SensorState.Stopped);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SensorEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<SensorEvent>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            itemDropped: _ => Interlocked.Increment(ref _droppedEvents));
        _subscribers[id] = channel;

        try
        {
            await foreach (var sensorEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return sensorEvent;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Best-effort teardown.
        }

        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryComplete();  // end any in-flight ReadEventsAsync enumerations
        }
    }

    // Produce-point. C2 adds the ReadEventsAsync multicast fan-out (+ DroppedEvents on 100-behind).
    internal void Emit(SensorEvent sensorEvent)
    {
        _bus.SensorEvents.TryWrite(sensorEvent);
        Interlocked.Increment(ref _eventsEmitted);
        Interlocked.Exchange(ref _lastEventAtTicks, DateTimeOffset.UtcNow.UtcTicks);

        // §5.3 multicast: fan out to every active ReadEventsAsync subscriber. DropOldest + itemDropped
        // counts a drop into DroppedEvents when a consumer is >100 behind (never blocks production).
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(sensorEvent);
        }
    }

    internal static SensorEvent ToSensorEvent(ScGameEvent gameEvent)
    {
        var features = ImmutableDictionary<string, double>.Empty;
        if (gameEvent.Kind == ScEventKind.LandingImpact &&
            StarCitizenEventParser.TryGetRelativeVelocityMagnitude(gameEvent.SourceLine, out var magnitude))
        {
            features = features.Add("relativeVelocityMagnitude", magnitude);
        }

        return new SensorEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SensorId = LogSensorId,
            SensorKind = SensorKind.Log,
            EventType = MapEventType(gameEvent.Kind),
            Timestamp = gameEvent.Timestamp,
            Intensity = gameEvent.Intensity,
            Duration = gameEvent.Duration,
            Features = features,
        };
    }

    private static string MapEventType(ScEventKind kind) => kind switch
    {
        ScEventKind.QuantumSpoolStarted => "log.quantum_spool_start",
        ScEventKind.QuantumSpoolEnded => "log.quantum_spool_end",
        ScEventKind.LandingImpact => "log.landing_impact_candidate",
        ScEventKind.AtmosphereEntered => "log.atmosphere_entered",
        ScEventKind.AtmosphereExited => "log.atmosphere_exited",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unmapped ScEventKind."),
    };

    private void OnLineRead(object? sender, string line)
    {
        var gameEvent = _patternLibrary.Current.Parse(line);
        if (gameEvent is not null)
        {
            Emit(ToSensorEvent(gameEvent));
        }
    }

    private void OnFaulted(object? sender, string message)
    {
        var previous = Health;
        _lastFault = message;
        _isHealthy = false;
        HealthChanged?.Invoke(this, new SensorHealthChangedEventArgs { Previous = previous, Current = Health });
    }

    // Sets state under the gate, then raises StateChanged AFTER releasing — so State already reflects the
    // new value when subscribers observe it, and no event is raised while holding the lock.
    private void TransitionTo(SensorState next)
    {
        SensorState previous;
        lock (_gate)
        {
            if (_state == next)
            {
                return;
            }

            previous = _state;
            _state = next;
        }

        StateChanged?.Invoke(this, new SensorStateChangedEventArgs { Previous = previous, Current = next });
    }
}
