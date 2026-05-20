using System.Threading.Channels;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Sensors;

namespace Moza.ScLink.Core.Bus;

/// <summary>
/// In-process event pipeline (PRP §2.7): three bounded channels carrying sensor evidence, fused game
/// events, and force commands. Sensors write <see cref="SensorEvent"/>s; the fusion engine reads them
/// and writes <see cref="GameEvent"/>s; the effect resolver reads game events and writes
/// <see cref="ForceCommand"/>s; the output worker reads force commands. Registered as a singleton.
/// </summary>
public interface IEventBus
{
    /// <summary>Writer for sensor evidence events (multi-writer; bounded 1024, DropOldest).</summary>
    ChannelWriter<SensorEvent> SensorEvents { get; }

    /// <summary>Reader for sensor evidence events (multi-reader: fusion engine plus diagnostic tap).</summary>
    ChannelReader<SensorEvent> SensorEventReader { get; }

    /// <summary>Writer for fused game events (single-writer: fusion engine; bounded 256, DropOldest).</summary>
    ChannelWriter<GameEvent> GameEvents { get; }

    /// <summary>Reader for fused game events (multi-reader: effect resolver plus diagnostic tap).</summary>
    ChannelReader<GameEvent> GameEventReader { get; }

    /// <summary>Writer for force commands (single-writer: effect resolver; bounded 64, Wait — backpressure).</summary>
    ChannelWriter<ForceCommand> ForceCommands { get; }

    /// <summary>Reader for force commands (single-reader: output worker).</summary>
    ChannelReader<ForceCommand> ForceCommandReader { get; }

    /// <summary>Observability counters for the three channels.</summary>
    EventBusMetrics Metrics { get; }
}
