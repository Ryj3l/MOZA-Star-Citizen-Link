using System.IO;
using FluentAssertions;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Fusion.Rules;
using static Moza.ScLink.Fusion.Tests.RuleJson;

namespace Moza.ScLink.Fusion.Tests;

public sealed class RuleLibraryTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"moza-rules-lib-{Guid.NewGuid():N}.json");

    private static string Valid(string ruleId = "r1", string produces = "WeaponFireGeneric") =>
        Rule(ruleId, produces, "weapon-fire", 0, 0.6, Requirement("Log", "log.weapon_fire"));

    [Fact]
    public void ShippedPhase1RulesLoadAllEight()
    {
        // Validates the DEPLOYED Rules/phase1-rules.json is internally consistent (all 8 pass validation),
        // not just the loading mechanism — a future bad edit (e.g. an unknown producesEventType) trips this.
        using var library = RuleLibrary.LoadDefault();

        library.Current.Should().HaveCount(8);
    }

    [Fact]
    public void HappyPathLoadsRulesAndConvertsFields()
    {
        Write(_path, Document(1,
            Rule("landing", "LandingGearContact", "landing-impact", 750, 0.6,
                Requirement("Log", "log.landing_impact_candidate")),
            Valid("weapon")));
        using var library = new RuleLibrary(_path, FastDebounce);

        library.Current.Should().HaveCount(2);
        var landing = library.Current.Single(r => r.RuleId == "landing");
        landing.ProducesEventType.Should().Be(GameEventType.LandingGearContact);
        landing.EvidenceWindow.Should().Be(TimeSpan.FromMilliseconds(750));
        landing.SuppressionKey.Should().Be("landing-impact");
        landing.Requirements.Should().ContainSingle();
        landing.Requirements[0].Kind.Should().Be(SensorKind.Log);
    }

    [Fact]
    public void SchemaVersionMismatchLogsWarning()
    {
        // schemaVersion != 1 is a structural failure: "load defaults" (empty) on initial load. The warning
        // is emitted via AppLog (Serilog); the observable behaviour asserted here is the empty fallback.
        Write(_path, Document(2, Valid()));
        using var library = new RuleLibrary(_path, FastDebounce);

        library.Current.Should().BeEmpty();
    }

    [Fact]
    public void MissingFileYieldsEmptyRuleSet()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"moza-rules-missing-{Guid.NewGuid():N}.json");
        using var library = new RuleLibrary(missing, FastDebounce);

        library.Current.Should().BeEmpty();
    }

    [Fact]
    public void NullDocumentYieldsEmptyRuleSet()
    {
        Write(_path, "null");
        using var library = new RuleLibrary(_path, FastDebounce);

        library.Current.Should().BeEmpty();
    }

    [Theory]
    [InlineData("emptyId")]
    [InlineData("badProducesEventType")]
    [InlineData("badRequirementKind")]
    [InlineData("minConfidenceHigh")]
    [InlineData("minConfidenceNegative")]
    [InlineData("noRequirements")]
    public void InvalidRuleIsRejected(string variant)
    {
        var rule = variant switch
        {
            "emptyId" => Rule("", "WeaponFireGeneric", "k", 0, 0.6, Requirement("Log", "log.x")),
            "badProducesEventType" => Rule("r", "NotARealEvent", "k", 0, 0.6, Requirement("Log", "log.x")),
            "badRequirementKind" => Rule("r", "WeaponFireGeneric", "k", 0, 0.6, Requirement("Smell", "log.x")),
            "minConfidenceHigh" => Rule("r", "WeaponFireGeneric", "k", 0, 1.5, Requirement("Log", "log.x")),
            "minConfidenceNegative" => Rule("r", "WeaponFireGeneric", "k", 0, -0.1, Requirement("Log", "log.x")),
            "noRequirements" => Rule("r", "WeaponFireGeneric", "k", 0, 0.6),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        Write(_path, Document(1, rule));
        using var library = new RuleLibrary(_path, FastDebounce);

        library.Current.Should().BeEmpty();
    }

    [Fact]
    public void InvalidRuleRejectedWhileValidRuleSurvives()
    {
        Write(_path, Document(1, Valid("good"), Rule("bad", "NotARealEvent", "k", 0, 0.6, Requirement("Log", "log.x"))));
        using var library = new RuleLibrary(_path, FastDebounce);

        // Distinguishing claim: the valid rule survives and the invalid one is dropped — not count 2,
        // not the wrong one dropped.
        library.Current.Should().ContainSingle();
        library.Current[0].RuleId.Should().Be("good");
    }

    [Fact]
    public async Task MalformedJsonFallsBackToPreviousGood()
    {
        Write(_path, Document(1, Valid("keep")));
        using var library = new RuleLibrary(_path, FastDebounce);
        var changedCount = 0;
        library.Changed += (_, _) => Interlocked.Increment(ref changedCount);

        library.Current.Should().ContainSingle();

        Write(_path, "{ not valid json");
        await Task.Delay(400);  // debounce window + reload attempt

        library.Current.Should().ContainSingle();              // (a) prior retained, not degraded
        library.Current[0].RuleId.Should().Be("keep");
        changedCount.Should().Be(0);                            // (b) no misleading change signal
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
        }
    }
}
