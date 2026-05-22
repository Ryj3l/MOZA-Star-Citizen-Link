using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;

namespace Moza.ScLink.Effects;

/// <summary>
/// State owner for the safety stage (Fork 3, T-15): tracks the currently active effects (with passive
/// duration-expiry) and the recent per-effect intensity history, builds a <see cref="SafetyContext"/> per
/// command, delegates the policy decision to an <see cref="ISafetyLimiter"/>, and updates its tracking from the
/// limiter's output. Stateless policy lives in the limiter; bus I/O lives in <c>EffectResolverService</c>;
/// this type owns the mutable bookkeeping in between.
/// </summary>
/// <remarks>
/// Called only from <c>EffectResolverService</c>'s single consume loop, so it is single-threaded by
/// construction and holds no locks (matching the single-writer ForceCommands channel). Time comes from each
/// command's <see cref="ForceCommand.IssuedAt"/> (event-time) — behaviour is clock-free and deterministic, and
/// expiry is evaluated lazily on each <see cref="Process"/> call rather than by a timer. Auto-stopping aged
/// sustained effects is out of scope (issue #50, T-16).
/// </remarks>
public sealed class SafetyLimiterStage
{
    private readonly ISafetyLimiter _limiter;

    // Active effects keyed by CommandId — the identity the limiter preempts by (Fork 1). Non-sustained entries
    // expire by duration; sustained entries leave only on a matching StopEffectCommand.
    private readonly Dictionary<string, PlayEffectCommand> _active = new(StringComparer.Ordinal);

    // Most recent admitted snapshot per effect id, for the rate-of-change limit. Admitted-only (rejected
    // commands do not update it) and untrimmed (the catalog id set is bounded; a stale entry self-neutralizes
    // because its large elapsed time permits an unrestricted delta).
    private readonly Dictionary<string, CommandSnapshot> _history = new(StringComparer.Ordinal);

    public SafetyLimiterStage(ISafetyLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);
        _limiter = limiter;
    }

    /// <summary>
    /// Evaluates <paramref name="command"/> against the current safety state for <paramref name="device"/>,
    /// returning the command(s) admitted to the output channel and updating active-effect and history tracking.
    /// </summary>
    public IReadOnlyList<ForceCommand> Process(ForceCommand command, DeviceCapabilities device)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(device);

        var now = command.IssuedAt;
        ExpireElapsed(now);

        var context = new SafetyContext(
            _active.Values.ToList(),
            device,
            now,
            new Dictionary<string, CommandSnapshot>(_history));

        var result = _limiter.Apply(command, context);

        foreach (var admitted in result)
        {
            ApplyToState(admitted);
        }

        return result;
    }

    // Drop non-sustained effects whose duration has elapsed by `now`. Sustained effects never time-expire —
    // they are removed only by a matching StopEffectCommand.
    private void ExpireElapsed(DateTimeOffset now)
    {
        List<string>? expired = null;
        foreach (var (key, play) in _active)
        {
            if (!play.Effect.IsSustained && play.IssuedAt + play.Effect.Duration <= now)
            {
                (expired ??= []).Add(key);
            }
        }

        foreach (var key in expired ?? Enumerable.Empty<string>())
        {
            _active.Remove(key);
        }
    }

    private void ApplyToState(ForceCommand command)
    {
        switch (command)
        {
            case PlayEffectCommand play:
                _active[play.CommandId] = play;
                _history[play.Effect.EffectId] = new CommandSnapshot(play.FinalIntensity, play.IssuedAt);
                break;

            case StopEffectCommand stop:
                RemoveStopped(stop.StateKey);
                break;

            default:
                // Other command kinds (e.g. StopAllCommand, a T-16 e-stop concern) carry no per-effect state
                // for this stage to track and pass through without mutating the active set.
                break;
        }
    }

    // Dual-shape removal: a preemption stop keys by the preempted command's CommandId; a resolver-issued
    // sustained stop keys by the effect's StateKey. Try the CommandId key first, then match StateKey.
    private void RemoveStopped(string key)
    {
        if (_active.Remove(key))
        {
            return;
        }

        foreach (var (commandId, play) in _active)
        {
            if (string.Equals(play.Effect.StateKey, key, StringComparison.Ordinal))
            {
                _active.Remove(commandId);
                return;
            }
        }
    }
}
