using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MozaStarCitizen.App.Diagnostics;
using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.Parsing;

public sealed class StarCitizenEventParser
{
    private static readonly Regex RelativeVelocityPattern = new(
        @"Relative Vel:\s*x:\s*(?<x>[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?),\s*y:\s*(?<y>[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?),\s*z:\s*(?<z>[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<CompiledPattern> _patterns;

    private StarCitizenEventParser(IEnumerable<EventPattern> patterns)
    {
        _patterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p.Kind) && !string.IsNullOrWhiteSpace(p.Pattern))
            .Select(CompiledPattern.Create)
            .ToArray();
    }

    public int PatternCount => _patterns.Count;

    public static StarCitizenEventParser LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "event-patterns.json");
        if (!File.Exists(path))
        {
            AppLog.Write($"Event pattern file was not found: {path}");
            return new StarCitizenEventParser([]);
        }

        var json = File.ReadAllText(path);
        var patterns = JsonSerializer.Deserialize<EventPattern[]>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var parser = new StarCitizenEventParser(patterns);
        AppLog.Write($"Loaded {parser.PatternCount} Star Citizen event pattern(s) from {path}.");
        return parser;
    }

    public ScGameEvent? Parse(string line)
    {
        if (line.Contains("No Route loaded", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("[QuantumTravel]", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var pattern in _patterns)
        {
            var match = pattern.Regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var intensity = ResolveIntensity(pattern, match, line);

            return new ScGameEvent(
                pattern.Kind,
                pattern.Name,
                Math.Clamp(intensity, 0, 1),
                TimeSpan.FromMilliseconds(Math.Max(0, pattern.DurationMs)),
                line,
                DateTimeOffset.Now);
        }

        return null;
    }

    private static double ResolveIntensity(CompiledPattern pattern, Match match, string line)
    {
        var intensity = pattern.Intensity;
        if (match.Groups["intensity"] is { Success: true } group &&
            double.TryParse(group.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedIntensity))
        {
            return Math.Clamp(parsedIntensity, 0, 1);
        }

        if (pattern.Kind == ScEventKind.LandingImpact &&
            TryGetRelativeVelocityMagnitude(line, out var relativeSpeed))
        {
            intensity = Math.Max(intensity, ScaleImpactIntensity(relativeSpeed));
        }

        return Math.Clamp(intensity, 0, 1);
    }

    private static bool TryGetRelativeVelocityMagnitude(string line, out double magnitude)
    {
        magnitude = 0;

        var match = RelativeVelocityPattern.Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(match.Groups["z"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            return false;
        }

        magnitude = Math.Sqrt((x * x) + (y * y) + (z * z));
        return true;
    }

    private static double ScaleImpactIntensity(double relativeSpeed)
    {
        const double minimumImpactSpeed = 6;
        const double maximumImpactSpeed = 45;
        const double minimumIntensity = 0.35;

        var normalized = (relativeSpeed - minimumImpactSpeed) / (maximumImpactSpeed - minimumImpactSpeed);
        return minimumIntensity + (Math.Clamp(normalized, 0, 1) * (1 - minimumIntensity));
    }

    private sealed record CompiledPattern(
        ScEventKind Kind,
        string Name,
        Regex Regex,
        double Intensity,
        int DurationMs)
    {
        public static CompiledPattern Create(EventPattern pattern)
        {
            if (!Enum.TryParse<ScEventKind>(pattern.Kind, ignoreCase: true, out var kind))
            {
                throw new InvalidOperationException($"Unknown event kind '{pattern.Kind}'.");
            }

            return new CompiledPattern(
                kind,
                pattern.Name,
                new Regex(pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
                pattern.Intensity,
                pattern.DurationMs);
        }
    }
}
