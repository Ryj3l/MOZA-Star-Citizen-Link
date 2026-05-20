using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moza.ScLink.Core.Diagnostics;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.DirectInput;

/// <summary>
/// JSON-driven allowlist of force-feedback devices the app may drive (PRP §13.3). Loaded from
/// <c>device-allowlist.json</c> next to the executable. Anything not matched classifies as
/// <see cref="DeviceModel.Unknown"/> and is never driven. Replaces the transitional
/// <c>DeviceClassifier</c>.
/// </summary>
public sealed class DeviceAllowlist
{
    /// <summary>Allowlist file name, resolved against <see cref="AppContext.BaseDirectory"/>.</summary>
    public const string FileName = "device-allowlist.json";

    private const string ContainsAnyCaseInsensitive = "containsAnyCaseInsensitive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<CompiledRule> _rules;

    private DeviceAllowlist(IReadOnlyList<CompiledRule> rules) => _rules = rules;

    /// <summary>
    /// Loads the allowlist from <c>device-allowlist.json</c> beside the executable. A missing file
    /// yields an empty allowlist — every device classifies as <see cref="DeviceModel.Unknown"/>, so the
    /// app fails safe (PRP §13.3 forbids driving unidentified devices).
    /// </summary>
    public static DeviceAllowlist LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path))
        {
            AppLog.Write($"Device allowlist file was not found: {path}. No force-feedback devices will be allowlisted.");
            return new DeviceAllowlist([]);
        }

        var allowlist = FromJson(File.ReadAllText(path));
        AppLog.Write($"Loaded device allowlist with {allowlist._rules.Count} model rule(s) from {path}.");
        return allowlist;
    }

    /// <summary>
    /// Parses an allowlist from a JSON string (the file-free entry point used by tests). Rows whose
    /// <c>model</c> is unparseable/<see cref="DeviceModel.Unknown"/>, whose <c>matchMode</c> is
    /// unrecognized, or which carry no usable patterns are dropped (fail-safe: never over-permit) and
    /// each drop is logged with its reason.
    /// </summary>
    public static DeviceAllowlist FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<DeviceAllowlistDocument>(json, JsonOptions);
        var rules = (document?.AllowedDeviceModels ?? [])
            .Select(CompiledRule.TryCreate)
            .OfType<CompiledRule>()
            .ToArray();
        return new DeviceAllowlist(rules);
    }

    /// <summary>
    /// Classifies a DirectInput product name. Null/empty/whitespace names and names matching no rule
    /// return <see cref="DeviceModel.Unknown"/>. First matching rule (declaration order) wins.
    /// </summary>
    public DeviceModel Classify(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return DeviceModel.Unknown;
        }

        foreach (var rule in _rules)
        {
            if (rule.Matches(productName))
            {
                return rule.Model;
            }
        }

        return DeviceModel.Unknown;
    }

    private sealed record CompiledRule(DeviceModel Model, IReadOnlyList<string> Patterns)
    {
        public static CompiledRule? TryCreate(DeviceAllowlistModel dto)
        {
            if (!Enum.TryParse<DeviceModel>(dto.Model, ignoreCase: true, out var model))
            {
                AppLog.Write($"Device allowlist: dropping rule with unparseable model '{dto.Model}'.");
                return null;
            }

            if (model == DeviceModel.Unknown)
            {
                AppLog.Write("Device allowlist: dropping rule with model 'Unknown' (Unknown cannot be allowlisted).");
                return null;
            }

            if (!string.Equals(dto.MatchMode, ContainsAnyCaseInsensitive, StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Write($"Device allowlist: dropping '{model}' rule with unrecognized matchMode '{dto.MatchMode}'.");
                return null;
            }

            var patterns = dto.ProductNamePatterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            if (patterns.Length == 0)
            {
                AppLog.Write($"Device allowlist: dropping '{model}' rule with no usable product-name patterns.");
                return null;
            }

            return new CompiledRule(model, patterns);
        }

        public bool Matches(string productName) =>
            Patterns.Any(p => productName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// JSON shape of <c>device-allowlist.json</c>. Consumed by <see cref="DeviceAllowlist"/>.
/// Uses <c>init</c>-only <see cref="IReadOnlyList{T}"/> properties: <c>init</c> keeps CA2227 quiet,
/// <see cref="IReadOnlyList{T}"/> (not <c>List</c>) keeps CA1002 quiet — both required under
/// <c>latest-Recommended</c> + TreatWarningsAsErrors. System.Text.Json deserializes these natively.
/// </summary>
public sealed class DeviceAllowlistDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("allowedDeviceModels")]
    public IReadOnlyList<DeviceAllowlistModel> AllowedDeviceModels { get; init; } = [];

    /// <summary>
    /// Reserved for future denylist semantics; not enforced in T-08. Precedence and match
    /// semantics require a design decision before implementation — see issue #40.
    /// </summary>
    [JsonPropertyName("denylistOverride")]
    public IReadOnlyList<string> DenylistOverride { get; init; } = [];
}

/// <summary>One allowlisted model rule from <c>device-allowlist.json</c>.</summary>
public sealed class DeviceAllowlistModel
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("productNamePatterns")]
    public IReadOnlyList<string> ProductNamePatterns { get; init; } = [];

    [JsonPropertyName("matchMode")]
    public string MatchMode { get; init; } = string.Empty;
}
