using System.Collections.Immutable;
using Microsoft.Extensions.Hosting;
using Moza.ScLink.Core.Bus;
using Moza.ScLink.Core.Events;
using Moza.ScLink.Core.Sensors;
using Moza.ScLink.Fusion.Rules;

namespace Moza.ScLink.Fusion;

/// <summary>
/// Rule-based fusion engine (PRP §6): the sole producer of <see cref="GameEvent"/>s. Reads
/// <see cref="SensorEvent"/>s from <see cref="IEventBus.SensorEventReader"/>, evaluates the hot-reloadable
/// <see cref="RuleLibrary"/> against a per-rule sliding evidence buffer, and publishes a confirmed
/// <see cref="GameEvent"/> to <see cref="IEventBus.GameEvents"/> when a rule's confidence clears its
/// threshold and its suppression key is not within a recent fire window.
/// </summary>
/// <remarks>
/// MIGRATION (T-12, tracked in #43/#45): registered as a hosted service but dormant until the generic
/// host starts — same Option-B staging as <c>LogSensor</c> (T-11) and <c>EffectCatalog</c> (T-13).
/// <para>
/// Suppression and evidence windows are measured against <see cref="SensorEvent.Timestamp"/> (event time),
/// not wall-clock, so windowed behaviour is deterministic and the legacy 750 ms landing-impact debounce
/// (<c>ForceFeedbackController</c>, first-wins-then-suppress) is preserved by the <c>landing-impact</c>
/// rule without a clock dependency. Phase 1 rules are single-sensor (confidence 0.0 or 1.0); the buffer and
/// the <c>Matches</c> seam are structured for Phase-2 multi-sensor corroboration.
/// </para>
/// </remarks>
public sealed class FusionEngine : BackgroundService
{
    private readonly IEventBus _bus;
    private readonly RuleLibrary _rules;

    // Evidence accumulation, keyed by ruleId (NOT suppressionKey — keys are shared across rules and would
    // cross-contaminate distinct rules' evidence). Touched only from the single consumer loop / tests.
    private readonly Dictionary<string, List<SensorEvent>> _evidenceBuffer = new(StringComparer.Ordinal);

    // Debounce ledger, keyed by suppressionKey: a fire on any rule with this key blocks rules sharing it
    // within their window (PRP §6 / T-12 deliverable #5).
    private readonly Dictionary<string, DateTimeOffset> _lastFire = new(StringComparer.Ordinal);

    private readonly object _metricsGate = new();
    private readonly Dictionary<string, Counters> _metrics = new(StringComparer.Ordinal);

    public FusionEngine(IEventBus bus, RuleLibrary rules)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(rules);
        _bus = bus;
        _rules = rules;
    }

    /// <summary>Per-rule firing/suppression counters for the diagnostics panel (snapshot; thread-safe).</summary>
    public IReadOnlyDictionary<string, FusionRuleMetrics> Metrics
    {
        get
        {
            lock (_metricsGate)
            {
                return _metrics.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new FusionRuleMetrics(kvp.Value.Firings, kvp.Value.Suppressions),
                    StringComparer.Ordinal);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var sensorEvent in _bus.SensorEventReader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                ProcessEvent(sensorEvent);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: StopAsync cancels stoppingToken.
        }
    }

    /// <summary>
    /// Core evaluation, exercised directly by unit tests (no host, no bus plumbing). Buffers the event for
    /// every rule it is relevant to, recomputes confidence over the rule's (window-pruned) evidence, and
    /// fires a <see cref="GameEvent"/> when confidence clears the threshold and the suppression key is free.
    /// </summary>
    internal void ProcessEvent(SensorEvent sensorEvent)
    {
        ArgumentNullException.ThrowIfNull(sensorEvent);

        var rules = _rules.Current;
        var eventTime = sensorEvent.Timestamp;

        foreach (var rule in rules)
        {
            if (!MatchesAnyRequirement(rule, sensorEvent))
            {
                continue;  // this event is irrelevant to this rule
            }

            var buffer = GetBuffer(rule.RuleId);
            buffer.Add(sensorEvent);

            // Evidence older than this rule's window (relative to the current event time) has expired.
            // For a zero window only same-instant evidence survives.
            buffer.RemoveAll(e => eventTime - e.Timestamp > rule.EvidenceWindow);

            if (!TryComputeConfidence(rule, buffer, out var confidence) || confidence < rule.MinConfidence)
            {
                continue;
            }

            // Suppression: a fire on any rule sharing this key within the window blocks this one.
            if (_lastFire.TryGetValue(rule.SuppressionKey, out var last) && eventTime - last < rule.EvidenceWindow)
            {
                Bump(rule.RuleId, suppressed: true);
                continue;
            }

            Publish(rule, sensorEvent, confidence);
            _lastFire[rule.SuppressionKey] = eventTime;
            buffer.Clear();  // clear the relevant buffer entries on fire (T-12 deliverable #1)
            Bump(rule.RuleId, suppressed: false);
        }
    }

    private static bool MatchesAnyRequirement(FusionRule rule, SensorEvent sensorEvent)
    {
        foreach (var requirement in rule.Requirements)
        {
            if (Matches(requirement, sensorEvent))
            {
                return true;
            }
        }

        return false;
    }

    // Confidence = Σ(matched required weights) / Σ(required weights). Returns false when the rule has no
    // required requirements (cannot be confirmed in Phase 1). Phase 1 single-sensor rules yield 1.0.
    private static bool TryComputeConfidence(FusionRule rule, List<SensorEvent> buffer, out double confidence)
    {
        confidence = 0;
        double requiredWeight = 0;
        double matchedWeight = 0;

        foreach (var requirement in rule.Requirements)
        {
            if (!requirement.Required)
            {
                continue;
            }

            requiredWeight += requirement.Weight;
            if (buffer.Exists(e => Matches(requirement, e)))
            {
                matchedWeight += requirement.Weight;
            }
        }

        if (requiredWeight <= 0)
        {
            return false;
        }

        confidence = matchedWeight / requiredWeight;
        return true;
    }

    // Phase-2 plug-in seam: requirement matching. Phase 1 matches on sensor kind + exact event-type string.
    private static bool Matches(EvidenceRequirement requirement, SensorEvent sensorEvent) =>
        requirement.Kind == sensorEvent.SensorKind &&
        string.Equals(requirement.EventType, sensorEvent.EventType, StringComparison.Ordinal);

    private void Publish(FusionRule rule, SensorEvent triggering, double confidence)
    {
        var gameEvent = new GameEvent
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = rule.ProducesEventType,
            Timestamp = triggering.Timestamp,
            Confidence = confidence,
            Intensity = triggering.Intensity,
            Duration = triggering.Duration,
            Sources = ImmutableArray.Create(triggering.SensorId),
            ReasonCodes = ImmutableArray.Create(rule.RuleId),
        };

        _bus.GameEvents.TryWrite(gameEvent);
    }

    private List<SensorEvent> GetBuffer(string ruleId)
    {
        if (!_evidenceBuffer.TryGetValue(ruleId, out var buffer))
        {
            buffer = [];
            _evidenceBuffer[ruleId] = buffer;
        }

        return buffer;
    }

    private void Bump(string ruleId, bool suppressed)
    {
        lock (_metricsGate)
        {
            if (!_metrics.TryGetValue(ruleId, out var counters))
            {
                counters = new Counters();
                _metrics[ruleId] = counters;
            }

            if (suppressed)
            {
                counters.Suppressions++;
            }
            else
            {
                counters.Firings++;
            }
        }
    }

    private sealed class Counters
    {
        public long Firings;
        public long Suppressions;
    }
}

/// <summary>Immutable per-rule metrics snapshot exposed by <see cref="FusionEngine.Metrics"/>.</summary>
public sealed record FusionRuleMetrics(long Firings, long Suppressions);
