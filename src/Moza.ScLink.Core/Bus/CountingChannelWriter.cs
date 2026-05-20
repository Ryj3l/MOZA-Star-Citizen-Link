using System.Threading.Channels;

namespace Moza.ScLink.Core.Bus;

/// <summary>
/// Decorates a <see cref="ChannelWriter{T}"/> to count successful writes into
/// <see cref="EventBusMetrics.ChannelCounters"/>. Needed because <see cref="IEventBus"/> exposes the raw
/// writer, leaving no other boundary at which to count "published" items.
/// </summary>
internal sealed class CountingChannelWriter<T> : ChannelWriter<T>
{
    private readonly ChannelWriter<T> _inner;
    private readonly EventBusMetrics.ChannelCounters _counters;

    public CountingChannelWriter(ChannelWriter<T> inner, EventBusMetrics.ChannelCounters counters)
    {
        _inner = inner;
        _counters = counters;
    }

    public override bool TryWrite(T item)
    {
        var written = _inner.TryWrite(item);
        if (written)
        {
            _counters.RecordPublished();
        }

        return written;
    }

    public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
        _inner.WaitToWriteAsync(cancellationToken);

    public override async ValueTask WriteAsync(T item, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        _counters.RecordPublished();
    }

    public override bool TryComplete(Exception? error = null) => _inner.TryComplete(error);
}
