using System.Collections.Immutable;

namespace Moza.ScLink.App.ForceFeedback;

/// <summary>
/// Minimal thread-safe multicast subject for <see cref="PreviewedCommand"/>, hand-rolled on the BCL
/// <see cref="IObservable{T}"/>/<see cref="IObserver{T}"/> contracts to avoid a System.Reactive
/// dependency (which would need a hard-rule-5 DDR for a single ~40-line need). Observers are held in an
/// <see cref="ImmutableArray{T}"/> swapped under a lock; <see cref="Publish"/> snapshots the array under
/// the lock and iterates <em>outside</em> it, so a slow observer cannot block the publishing pipeline
/// thread and a <see cref="Subscribe"/>/unsubscribe concurrent with a publish cannot mutate the in-flight
/// snapshot. Single-producer in practice (the one <c>ForceCommandPipeline</c> reader thread calls
/// <see cref="Publish"/>); the lock guards against concurrent UI-thread Subscribe/Dispose.
/// </summary>
internal sealed class PreviewCommandSubject : IObservable<PreviewedCommand>
{
    private readonly object _gate = new();
    private ImmutableArray<IObserver<PreviewedCommand>> _observers =
        ImmutableArray<IObserver<PreviewedCommand>>.Empty;

    public IDisposable Subscribe(IObserver<PreviewedCommand> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            _observers = _observers.Add(observer);
        }

        return new Subscription(this, observer);
    }

    public void Publish(PreviewedCommand command)
    {
        ImmutableArray<IObserver<PreviewedCommand>> snapshot;
        lock (_gate)
        {
            snapshot = _observers;
        }

        foreach (var observer in snapshot)
        {
            observer.OnNext(command);
        }
    }

    private void Unsubscribe(IObserver<PreviewedCommand> observer)
    {
        lock (_gate)
        {
            _observers = _observers.Remove(observer);
        }
    }

    /// <summary>Unsubscribe handle returned from <see cref="Subscribe"/>; idempotent.</summary>
    private sealed class Subscription : IDisposable
    {
        private readonly IObserver<PreviewedCommand> _observer;
        private PreviewCommandSubject? _subject;

        public Subscription(PreviewCommandSubject subject, IObserver<PreviewedCommand> observer)
        {
            _subject = subject;
            _observer = observer;
        }

        public void Dispose()
        {
            // Only the first Dispose removes the observer; subsequent calls are no-ops.
            var subject = Interlocked.Exchange(ref _subject, null);
            subject?.Unsubscribe(_observer);
        }
    }
}
