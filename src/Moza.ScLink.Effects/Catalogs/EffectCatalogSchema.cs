namespace Moza.ScLink.Effects.Catalogs;

/// <summary>
/// Deserialization shape for an effect-catalog JSON document (e.g. <c>Catalogs/phase1.json</c>).
/// Mirrors the on-disk schema verbatim; effect-type/category vocabulary validation is performed by
/// <c>EffectCatalog</c>, and envelope-ms → <see cref="System.TimeSpan"/> / render-type interpretation
/// by T-14's resolver. The <c>$schema</c> decorator is intentionally not mapped.
/// </summary>
public sealed record EffectCatalogDocument
{
    public int SchemaVersion { get; init; }
    public string? CatalogId { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<EffectDefinition> Effects { get; init; } = [];
}

/// <summary>
/// One effect row. <see cref="Category"/> and <see cref="EffectType"/> are raw strings here and are
/// validated against their vocabularies by <c>EffectCatalog</c> (graceful per-effect rejection, not
/// a deserialization throw). All string fields are nullable so a malformed row is rejected with a
/// warning rather than throwing during load.
/// </summary>
public sealed record EffectDefinition
{
    public string? EffectId { get; init; }
    public string? DisplayName { get; init; }
    public string? Category { get; init; }
    public string? EffectType { get; init; }
    public double BaseIntensity { get; init; }
    public double FrequencyHz { get; init; }
    public int DurationMs { get; init; }
    public double DirectionX { get; init; }
    public double DirectionY { get; init; }
    public EffectEnvelopeDefinition? Envelope { get; init; }
    public bool IsSustained { get; init; }
    public string? StateKey { get; init; }
    public IReadOnlyList<string> StoppedBy { get; init; } = [];
    public string? Notes { get; init; }
}

/// <summary>
/// ADSR-style envelope in milliseconds. T-14's resolver maps these to
/// <see cref="System.TimeSpan"/> on the Core <c>ForceEnvelope</c>.
/// </summary>
public sealed record EffectEnvelopeDefinition
{
    public int AttackMs { get; init; }
    public int HoldMs { get; init; }
    public int DecayMs { get; init; }
    public int ReleaseMs { get; init; }
    public double AttackLevel { get; init; }
    public double SustainLevel { get; init; }
}
