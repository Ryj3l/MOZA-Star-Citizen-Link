namespace Moza.ScLink.Core.Bus;

/// <summary>
/// Observability counters for the three <see cref="EventBus"/> channels (PRP §2.7): cumulative published
/// and dropped counts per channel, plus live depth. Drop-rate evaluation is a pure static helper so it
/// can be unit-tested without a running host (the App-layer monitor owns the 60-second cadence).
/// </summary>
public sealed class EventBusMetrics
{
    /// <summary>Counters for the SensorEvent channel.</summary>
    public ChannelCounters SensorEvents { get; }

    /// <summary>Counters for the GameEvent channel.</summary>
    public ChannelCounters GameEvents { get; }

    /// <summary>Counters for the ForceCommand channel.</summary>
    public ChannelCounters ForceCommands { get; }

    internal EventBusMetrics(Func<int> sensorDepth, Func<int> gameDepth, Func<int> forceDepth)
    {
        SensorEvents = new ChannelCounters(sensorDepth);
        GameEvents = new ChannelCounters(gameDepth);
        ForceCommands = new ChannelCounters(forceDepth);
    }

    /// <summary>
    /// Whether the drop rate over a sample window exceeds <paramref name="threshold"/> (e.g. 0.01 for 1%).
    /// Rate is <c>dropped / published</c>; returns <see langword="false"/> when nothing was published.
    /// </summary>
    public static bool ExceedsDropRate(long publishedInWindow, long droppedInWindow, double threshold)
    {
        if (publishedInWindow <= 0)
        {
            return false;
        }

        return (double)droppedInWindow / publishedInWindow > threshold;
    }

    /// <summary>Published/dropped/depth counters for a single channel. Counters are thread-safe.</summary>
    public sealed class ChannelCounters
    {
        private readonly Func<int> _depth;
        private long _published;
        private long _dropped;

        internal ChannelCounters(Func<int> depth) => _depth = depth;

        /// <summary>Cumulative count of items successfully written to the channel.</summary>
        public long Published => Interlocked.Read(ref _published);

        /// <summary>Cumulative count of items evicted by DropOldest (always 0 for the Wait channel).</summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        /// <summary>Current number of buffered items in the channel.</summary>
        public int Depth => _depth();

        internal void RecordPublished() => Interlocked.Increment(ref _published);

        internal void RecordDropped() => Interlocked.Increment(ref _dropped);
    }
}
