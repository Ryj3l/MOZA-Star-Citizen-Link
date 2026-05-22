using Microsoft.Extensions.Logging;
using Moza.ScLink.Core.Devices;
using Moza.ScLink.Core.Effects;
using Moza.ScLink.Core.Safety;

namespace Moza.ScLink.Effects;

/// <summary>
/// Stateless safety policy (PRP §5.8): evaluates a single resolved <see cref="ForceCommand"/> against the
/// mandatory safety constants (<see cref="Moza.ScLink.Core.Safety.SafetyLimits"/>) and returns the command(s)
/// admitted to the output channel. The contract returns a list so a single input can yield zero, one, or
/// several outputs — matching the simultaneous-effect cap, where admitting a new effect at capacity also
/// emits a <see cref="StopEffectCommand"/> to preempt the oldest non-sustained effect (stop precedes play).
/// </summary>
/// <remarks>
/// The list shape mirrors <see cref="Moza.ScLink.Core.Resolver.IEffectResolver.Resolve"/> (the same pipeline
/// convention). Empty = rejected; single = clamped or passed through; multiple = preemption
/// (<c>[StopEffectCommand(oldest), &lt;admitted command&gt;]</c>, order significant).
/// </remarks>
public interface ISafetyLimiter
{
    /// <summary>Evaluates <paramref name="command"/> against <paramref name="context"/> and returns the admitted command(s).</summary>
    IReadOnlyList<ForceCommand> Apply(ForceCommand command, SafetyContext context);
}

/// <summary>
/// Per-command evaluation context for <see cref="ISafetyLimiter.Apply"/>: the currently active effects, the
/// target device's capabilities, the evaluation time, and recent per-effect intensity history (for the
/// rate-of-change limit). Built and owned by <c>SafetyLimiterStage</c>; the limiter itself stays stateless.
/// </summary>
/// <param name="ActiveEffects">Effects currently playing, used for the simultaneous-effect cap and preemption.</param>
/// <param name="Device">The target device's capabilities; <see cref="DeviceCapabilities.MaxIntensityRecommended"/> is the intensity ceiling.</param>
/// <param name="Now">Evaluation time, sourced from the command's <see cref="ForceCommand.IssuedAt"/> so behaviour is clock-free and deterministic.</param>
/// <param name="RecentIntensityHistory">Most recent <see cref="CommandSnapshot"/> per effect id, keyed by <see cref="ForceEffect.EffectId"/>; drives the per-second rate-of-change limit.</param>
public sealed record SafetyContext(
    IReadOnlyList<PlayEffectCommand> ActiveEffects,
    DeviceCapabilities Device,
    DateTimeOffset Now,
    IReadOnlyDictionary<string, CommandSnapshot> RecentIntensityHistory);

/// <summary>
/// A prior command's intensity and the time it was issued. The rate-of-change limit needs both intensity and
/// time (Δintensity / Δtime), so history records this pair rather than intensity alone (Fork 2, T-15 plan).
/// </summary>
/// <param name="Intensity">The command's final intensity at <paramref name="At"/>.</param>
/// <param name="At">The time the command was issued (<see cref="ForceCommand.IssuedAt"/>).</param>
public sealed record CommandSnapshot(double Intensity, DateTimeOffset At);

/// <summary>
/// Pure, stateless implementation of <see cref="ISafetyLimiter"/> (PRP §5.8). Each enforcement that changes a
/// value logs a <see cref="LogLevel.Warning"/> carrying the original and clamped values (deliverable #3) via
/// <see cref="LoggerMessage"/> source-generated delegates (CA1848). The state some limits need (active
/// effects, prior-intensity history) arrives in the <see cref="SafetyContext"/> built by
/// <c>SafetyLimiterStage</c>; the limiter holds no mutable state of its own.
/// </summary>
public sealed class SafetyLimiter : ISafetyLimiter
{
    /// <summary>
    /// The sustained-cap predicate applies to effects sustained beyond this duration — see the
    /// <see cref="SafetyLimits.MaxSustainedIntensity"/> doc ("effects sustained longer than 5 seconds") and
    /// T-15 deliverable #2. Local to the limiter (it is this policy's threshold, not a shared safety floor),
    /// so it is deliberately not added to <see cref="SafetyLimits"/>.
    /// </summary>
    private static readonly TimeSpan SustainedDurationThreshold = TimeSpan.FromSeconds(5);

    private readonly ILogger<SafetyLimiter> _logger;

    public SafetyLimiter(ILogger<SafetyLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ForceCommand> Apply(ForceCommand command, SafetyContext context)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        // Stop commands carry no intensity or duration and consume no effect slot: pass through unchanged.
        if (command is not PlayEffectCommand play)
        {
            return [command];
        }

        var effect = play.Effect;
        var intensity = play.FinalIntensity;

        // 1. Device-cap clamp to [MinIntensity, device ceiling]. Redundant-but-defensive vs the gain stack (F3).
        var deviceClamped = Math.Clamp(intensity, SafetyLimits.MinIntensity, context.Device.MaxIntensityRecommended);
        if (deviceClamped != intensity)
        {
            Log.DeviceCapClamp(_logger, effect.EffectId, intensity, deviceClamped);
            intensity = deviceClamped;
        }

        // 2. Sustained cap: sustained effects (Duration == Zero, or > 5s) clamp to MaxSustainedIntensity (0.7).
        if (effect.IsSustained &&
            (effect.Duration == TimeSpan.Zero || effect.Duration > SustainedDurationThreshold) &&
            intensity > SafetyLimits.MaxSustainedIntensity)
        {
            Log.SustainedCap(_logger, effect.EffectId, intensity, SafetyLimits.MaxSustainedIntensity);
            intensity = SafetyLimits.MaxSustainedIntensity;
        }

        // 3. Rate-of-change: |Δintensity| / Δseconds capped at MaxIntensityRateOfChangePerSecond, measured
        //    against the last command for the same effect id. Skipped without a prior snapshot or measurable
        //    elapsed time (rate is undefined at Δtime <= 0).
        if (context.RecentIntensityHistory.TryGetValue(effect.EffectId, out var prior))
        {
            var elapsedSeconds = (context.Now - prior.At).TotalSeconds;
            if (elapsedSeconds > 0)
            {
                var maxDelta = SafetyLimits.MaxIntensityRateOfChangePerSecond * elapsedSeconds;
                var requestedDelta = intensity - prior.Intensity;
                if (Math.Abs(requestedDelta) > maxDelta)
                {
                    var rateClamped = prior.Intensity + Math.CopySign(maxDelta, requestedDelta);
                    Log.RateClamp(_logger, effect.EffectId, intensity, rateClamped);
                    intensity = rateClamped;
                }
            }
        }

        // A command whose intensity has been clamped to the floor produces no force: reject it (plan Fork 1).
        if (intensity <= SafetyLimits.MinIntensity)
        {
            Log.RejectedZeroIntensity(_logger, effect.EffectId, play.FinalIntensity);
            return [];
        }

        // 4. Duration cap: effects longer than the absolute maximum have their duration capped.
        if (effect.Duration > SafetyLimits.AbsoluteMaxEffectDuration)
        {
            Log.DurationCap(_logger, effect.EffectId, effect.Duration, SafetyLimits.AbsoluteMaxEffectDuration);
            effect = effect with { Duration = SafetyLimits.AbsoluteMaxEffectDuration };
        }

        var admitted = play with { Effect = effect, FinalIntensity = intensity };

        // 5. Simultaneous-effect cap: at capacity, preempt the oldest non-sustained active effect (stop precedes
        //    play). If every active effect is sustained, the cap cannot be honored while admitting, so reject.
        if (context.ActiveEffects.Count >= SafetyLimits.MaxSimultaneousEffects)
        {
            var preempted = OldestNonSustained(context.ActiveEffects);
            if (preempted is null)
            {
                Log.RejectedAtCap(_logger, effect.EffectId, context.ActiveEffects.Count);
                return [];
            }

            Log.Preempt(_logger, preempted.Effect.EffectId, effect.EffectId);

            // The preempted effect is identified by its command instance (its CommandId), not a category state
            // key — non-sustained effects have no StateKey. T-16's output worker dispatches StopEffectCommand
            // against both sustained state keys and command instance ids (see PR body, T-16 design input).
            var stop = new StopEffectCommand(preempted.CommandId)
            {
                CommandId = Guid.NewGuid().ToString(),
                IssuedAt = context.Now,
            };
            return [stop, admitted];
        }

        return [admitted];
    }

    private static PlayEffectCommand? OldestNonSustained(IReadOnlyList<PlayEffectCommand> active)
    {
        PlayEffectCommand? oldest = null;
        foreach (var candidate in active)
        {
            if (candidate.Effect.IsSustained)
            {
                continue;
            }

            if (oldest is null || candidate.IssuedAt < oldest.IssuedAt)
            {
                oldest = candidate;
            }
        }

        return oldest;
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, double, double, Exception?> _deviceCapClamp =
            LoggerMessage.Define<string, double, double>(
                LogLevel.Warning,
                new EventId(1, nameof(DeviceCapClamp)),
                "Safety limiter clamped '{EffectId}' intensity to the device ceiling: {Original} -> {Clamped}");

        private static readonly Action<ILogger, string, double, double, Exception?> _sustainedCap =
            LoggerMessage.Define<string, double, double>(
                LogLevel.Warning,
                new EventId(2, nameof(SustainedCap)),
                "Safety limiter clamped sustained effect '{EffectId}' intensity to the sustained ceiling: {Original} -> {Clamped}");

        private static readonly Action<ILogger, string, double, double, Exception?> _rateClamp =
            LoggerMessage.Define<string, double, double>(
                LogLevel.Warning,
                new EventId(3, nameof(RateClamp)),
                "Safety limiter rate-limited '{EffectId}' intensity change: {Original} -> {Clamped}");

        private static readonly Action<ILogger, string, TimeSpan, TimeSpan, Exception?> _durationCap =
            LoggerMessage.Define<string, TimeSpan, TimeSpan>(
                LogLevel.Warning,
                new EventId(4, nameof(DurationCap)),
                "Safety limiter capped '{EffectId}' duration: {Original} -> {Clamped}");

        private static readonly Action<ILogger, string, string, Exception?> _preempt =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                new EventId(5, nameof(Preempt)),
                "Safety limiter preempted oldest non-sustained effect '{PreemptedEffectId}' to admit '{EffectId}' at the simultaneous-effect cap");

        private static readonly Action<ILogger, string, int, Exception?> _rejectedAtCap =
            LoggerMessage.Define<string, int>(
                LogLevel.Warning,
                new EventId(6, nameof(RejectedAtCap)),
                "Safety limiter rejected '{EffectId}': simultaneous-effect cap reached with {ActiveCount} active effects, none non-sustained to preempt");

        private static readonly Action<ILogger, string, double, Exception?> _rejectedZeroIntensity =
            LoggerMessage.Define<string, double>(
                LogLevel.Warning,
                new EventId(7, nameof(RejectedZeroIntensity)),
                "Safety limiter rejected '{EffectId}': admitted intensity clamped to the floor (requested {Requested})");

        public static void DeviceCapClamp(ILogger logger, string effectId, double original, double clamped) =>
            _deviceCapClamp(logger, effectId, original, clamped, null);

        public static void SustainedCap(ILogger logger, string effectId, double original, double clamped) =>
            _sustainedCap(logger, effectId, original, clamped, null);

        public static void RateClamp(ILogger logger, string effectId, double original, double clamped) =>
            _rateClamp(logger, effectId, original, clamped, null);

        public static void DurationCap(ILogger logger, string effectId, TimeSpan original, TimeSpan clamped) =>
            _durationCap(logger, effectId, original, clamped, null);

        public static void Preempt(ILogger logger, string preemptedEffectId, string effectId) =>
            _preempt(logger, preemptedEffectId, effectId, null);

        public static void RejectedAtCap(ILogger logger, string effectId, int activeCount) =>
            _rejectedAtCap(logger, effectId, activeCount, null);

        public static void RejectedZeroIntensity(ILogger logger, string effectId, double requested) =>
            _rejectedZeroIntensity(logger, effectId, requested, null);
    }
}
