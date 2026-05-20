using System.Threading.Channels;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Sensors;

namespace Moza.ScLink.Core.Bus;

/// <summary>
/// Concrete <see cref="IEventBus"/>. Channel capacities, full-modes, and writer/reader cardinalities are
/// fixed by PRP §2.7. The two DropOldest channels route evicted items through an <c>itemDropped</c>
/// callback into <see cref="EventBusMetrics"/>; ForceCommand uses Wait (no drops) so it has no callback.
/// Exposed writers are wrapped in <see cref="CountingChannelWriter{T}"/> so published items are counted.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly Channel<SensorEvent> _sensorEvents;
    private readonly Channel<GameEvent> _gameEvents;
    private readonly Channel<ForceCommand> _forceCommands;
    private readonly EventBusMetrics _metrics;
    private readonly CountingChannelWriter<SensorEvent> _sensorWriter;
    private readonly CountingChannelWriter<GameEvent> _gameWriter;
    private readonly CountingChannelWriter<ForceCommand> _forceWriter;

    /// <summary>Creates the three bounded channels with their PRP §2.7 options and wires drop counting.</summary>
    public EventBus()
    {
        _sensorEvents = Channel.CreateBounded<SensorEvent>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = false,
            },
            itemDropped: OnSensorDropped);

        _gameEvents = Channel.CreateBounded<GameEvent>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = false,
            },
            itemDropped: OnGameDropped);

        _forceCommands = Channel.CreateBounded<ForceCommand>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = true,
            });

        _metrics = new EventBusMetrics(
            () => _sensorEvents.Reader.Count,
            () => _gameEvents.Reader.Count,
            () => _forceCommands.Reader.Count);

        _sensorWriter = new CountingChannelWriter<SensorEvent>(_sensorEvents.Writer, _metrics.SensorEvents);
        _gameWriter = new CountingChannelWriter<GameEvent>(_gameEvents.Writer, _metrics.GameEvents);
        _forceWriter = new CountingChannelWriter<ForceCommand>(_forceCommands.Writer, _metrics.ForceCommands);
    }

    /// <inheritdoc />
    public ChannelWriter<SensorEvent> SensorEvents => _sensorWriter;

    /// <inheritdoc />
    public ChannelReader<SensorEvent> SensorEventReader => _sensorEvents.Reader;

    /// <inheritdoc />
    public ChannelWriter<GameEvent> GameEvents => _gameWriter;

    /// <inheritdoc />
    public ChannelReader<GameEvent> GameEventReader => _gameEvents.Reader;

    /// <inheritdoc />
    public ChannelWriter<ForceCommand> ForceCommands => _forceWriter;

    /// <inheritdoc />
    public ChannelReader<ForceCommand> ForceCommandReader => _forceCommands.Reader;

    /// <inheritdoc />
    public EventBusMetrics Metrics => _metrics;

    private void OnSensorDropped(SensorEvent droppedEvent) => _metrics.SensorEvents.RecordDropped();

    private void OnGameDropped(GameEvent droppedEvent) => _metrics.GameEvents.RecordDropped();
}
