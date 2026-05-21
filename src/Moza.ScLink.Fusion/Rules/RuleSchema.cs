namespace Moza.ScLink.Fusion.Rules;

/// <summary>
/// Deserialization shape for a fusion-rules JSON document (e.g. <c>Rules/phase1-rules.json</c>).
/// Mirrors the on-disk schema verbatim; <see cref="FusionRuleDto.ProducesEventType"/> /
/// <see cref="EvidenceRequirementDto.Kind"/> enum parsing, <c>evidenceWindowMs</c> →
/// <see cref="System.TimeSpan"/> conversion, and per-rule validation are performed by
/// <c>RuleLibrary</c> (graceful per-rule rejection, not a deserialization throw). The
/// <c>$schema</c> decorator is intentionally not mapped.
/// </summary>
public sealed record FusionRuleDocument
{
    public int SchemaVersion { get; init; }
    public IReadOnlyList<FusionRuleDto> Rules { get; init; } = [];
}

/// <summary>
/// One rule row. <see cref="ProducesEventType"/> and <see cref="EvidenceRequirementDto.Kind"/> are raw
/// strings here and are validated against <c>GameEventType</c> / <c>SensorKind</c> by <c>RuleLibrary</c>.
/// String fields are nullable so a malformed row is rejected with a warning rather than throwing on load.
/// </summary>
public sealed record FusionRuleDto
{
    public string? RuleId { get; init; }
    public string? ProducesEventType { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<EvidenceRequirementDto> Requirements { get; init; } = [];
    public int EvidenceWindowMs { get; init; }
    public double MinConfidence { get; init; }
    public string? SuppressionKey { get; init; }
}

/// <summary>One evidence requirement row. <see cref="Kind"/> is validated against <c>SensorKind</c>.</summary>
public sealed record EvidenceRequirementDto
{
    public string? Kind { get; init; }
    public string? EventType { get; init; }
    public double Weight { get; init; }
    public bool Required { get; init; }
}
