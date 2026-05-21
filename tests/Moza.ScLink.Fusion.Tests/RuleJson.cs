using System.Globalization;
using System.IO;

namespace Moza.ScLink.Fusion.Tests;

// Shared JSON builders and file/timing helpers for the Fusion test suite. Doubles are formatted
// InvariantCulture so a comma-decimal locale never produces malformed JSON (mirrors EffectCatalogTests).
internal static class RuleJson
{
    public static readonly TimeSpan FastDebounce = TimeSpan.FromMilliseconds(100);

    public static string Requirement(string kind, string eventType, double weight = 1.0, bool required = true)
    {
        var w = weight.ToString(CultureInfo.InvariantCulture);
        var r = required ? "true" : "false";
        return $$"""{ "kind": "{{kind}}", "eventType": "{{eventType}}", "weight": {{w}}, "required": {{r}} }""";
    }

    public static string Rule(
        string ruleId,
        string producesEventType,
        string suppressionKey,
        int evidenceWindowMs,
        double minConfidence,
        params string[] requirements)
    {
        var mc = minConfidence.ToString(CultureInfo.InvariantCulture);
        return $$"""
            {
              "ruleId": "{{ruleId}}",
              "producesEventType": "{{producesEventType}}",
              "description": "test rule",
              "requirements": [ {{string.Join(",", requirements)}} ],
              "evidenceWindowMs": {{evidenceWindowMs}},
              "minConfidence": {{mc}},
              "suppressionKey": "{{suppressionKey}}"
            }
            """;
    }

    public static string Document(int schemaVersion, params string[] rules) =>
        $$"""{ "schemaVersion": {{schemaVersion}}, "rules": [ {{string.Join(",", rules)}} ] }""";

    public static void Write(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs);
        writer.Write(content);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
