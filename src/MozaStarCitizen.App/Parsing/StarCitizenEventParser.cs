using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.Parsing;

public sealed class StarCitizenEventParser
{
    private readonly IReadOnlyList<CompiledPattern> _patterns;

    private StarCitizenEventParser(IEnumerable<EventPattern> patterns)
    {
        _patterns = patterns
            .Where(p => !string.IsNullOrWhiteSpace(p.Kind) && !string.IsNullOrWhiteSpace(p.Pattern))
            .Select(CompiledPattern.Create)
            .ToArray();
    }

    public static StarCitizenEventParser LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "event-patterns.json");
        if (!File.Exists(path))
        {
            return new StarCitizenEventParser([]);
        }

        var json = File.ReadAllText(path);
        var patterns = JsonSerializer.Deserialize<EventPattern[]>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        return new StarCitizenEventParser(patterns);
    }

    public ScGameEvent? Parse(string line)
    {
        foreach (var pattern in _patterns)
        {
            var match = pattern.Regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var intensity = pattern.Intensity;
            if (match.Groups["intensity"] is { Success: true } group &&
                double.TryParse(group.Value, out var parsedIntensity))
            {
                intensity = Math.Clamp(parsedIntensity, 0, 1);
            }

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
