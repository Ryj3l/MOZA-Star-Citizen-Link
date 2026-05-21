using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Fusion.Rules;

/// <summary>
/// A validated fusion rule (domain shape, per T-12 deliverable #2). Produced from a
/// <see cref="FusionRuleDto"/> by <c>RuleLibrary</c> after enum parsing and validation.
/// </summary>
public sealed record FusionRule(
    string RuleId,
    GameEventType ProducesEventType,
    string Description,
    IReadOnlyList<EvidenceRequirement> Requirements,
    TimeSpan EvidenceWindow,
    double MinConfidence,
    string SuppressionKey);

/// <summary>
/// One requirement a rule places on incoming sensor evidence. <see cref="Required"/> requirements
/// contribute to the confidence denominator; optional ones (Phase 2) only boost confidence.
/// </summary>
public sealed record EvidenceRequirement(
    SensorKind Kind,
    string EventType,
    double Weight,
    bool Required);
