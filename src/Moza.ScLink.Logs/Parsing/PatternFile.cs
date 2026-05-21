using System.Text.Json.Serialization;

namespace Moza.ScLink.Logs.Parsing;

/// <summary>
/// Versioned wrapper for a <c>Patterns/*.json</c> file (<c>{ "schemaVersion": N, "patterns": [...] }</c>).
/// Replaces the prior bare-array shape so the pattern tooling and the versioned loader read a stable
/// envelope. <c>init</c>-only <see cref="IReadOnlyList{T}"/> stays clean under WL5/TWAE (CA2227/CA1002).
/// </summary>
public sealed class PatternFile
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("patterns")]
    public IReadOnlyList<EventPattern> Patterns { get; init; } = [];
}
