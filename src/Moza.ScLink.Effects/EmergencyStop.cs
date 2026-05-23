using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects;

/// <summary>
/// Authoritative emergency-stop state owner (T-16 PR1), implementing <see cref="IEmergencyStop"/>:
/// thread-safe, idempotent activation/clear with event notification. The actual force-halt is performed by
/// <c>ForceCommandPipeline</c>, which subscribes to <see cref="Activated"/>; this type owns only the state and
/// the transition signalling (three-clean-roles: state here, consume/halt-decision in the pipeline,
/// device-wide stop in the device).
/// </summary>
public sealed class EmergencyStop : IEmergencyStop
{
    private readonly ILogger<EmergencyStop> _logger;
    private readonly object _gate = new();

    // Read lock-free on the pipeline's per-command hot path; transitions are serialized under _gate.
    private volatile bool _isActive;

    public EmergencyStop(ILogger<EmergencyStop> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <inheritdoc />
    public event EventHandler<EmergencyStopActivatedEventArgs>? Activated;

    /// <inheritdoc />
    public event EventHandler? Cleared;

    /// <inheritdoc />
    public Task ActivateAsync(string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ct.ThrowIfCancellationRequested();

        // Decide under the lock (atomic test-and-set so concurrent activations raise exactly once);
        // raise the event outside it to avoid re-entrancy deadlock if a handler calls back in.
        EmergencyStopActivatedEventArgs? args = null;
        lock (_gate)
        {
            if (!_isActive)
            {
                _isActive = true;
                args = new EmergencyStopActivatedEventArgs
                {
                    Reason = reason,
                    ActivatedAt = DateTimeOffset.UtcNow,
                };
            }
        }

        if (args is not null)
        {
            Log.Activated(_logger, args.Reason);
            Activated?.Invoke(this, args);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var cleared = false;
        lock (_gate)
        {
            if (_isActive)
            {
                _isActive = false;
                cleared = true;
            }
        }

        if (cleared)
        {
            Log.Cleared(_logger);
            Cleared?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, Exception?> _activated =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(1, nameof(Activated)),
                "Emergency stop ACTIVATED (reason: {Reason})");

        private static readonly Action<ILogger, Exception?> _cleared =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(2, nameof(Cleared)),
                "Emergency stop cleared");

        public static void Activated(ILogger logger, string reason) => _activated(logger, reason, null);

        public static void Cleared(ILogger logger) => _cleared(logger, null);
    }
}
