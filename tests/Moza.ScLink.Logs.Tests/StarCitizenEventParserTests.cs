using System.Globalization;
using FluentAssertions;
using Moza.ScLink.Core.Models;
using Moza.ScLink.Logs.Parsing;

namespace Moza.ScLink.Logs.Tests;

/// <summary>
/// Characterization tests pinning StarCitizenEventParser's PRP §14.2 preserve-behaviors against the
/// CURRENT implementation, before the T-11 PatternLibrary refactor. Patterns are injected via the
/// internal ctor (InternalsVisibleTo) so assertions are independent of v0.json's regexes.
/// </summary>
public sealed class StarCitizenEventParserTests
{
    private static StarCitizenEventParser ParserWith(params EventPattern[] patterns) => new(patterns);

    private static StarCitizenEventParser ParserWith(TimeSpan matchTimeout, params EventPattern[] patterns) =>
        new(patterns, matchTimeout);

    [Theory]
    [InlineData(6.0, 0.35)]    // lower bound: ScaleImpactIntensity floor
    [InlineData(45.0, 1.0)]    // upper bound: full intensity
    [InlineData(25.5, 0.675)]  // midpoint of the 6->45 m/s linear curve: 0.35 + 0.5*0.65
    public void LandingImpactIntensityScalesWithRelativeVelocity(double velocity, double expectedIntensity)
    {
        // Base Intensity (0.2) is below every scaled boundary value, so ResolveIntensity's
        // Max(base, ScaleImpactIntensity(v)) yields the velocity-scaled value under test.
        var parser = ParserWith(new EventPattern
        {
            Kind = "LandingImpact",
            Name = "test-landing",
            Pattern = "FatalCollision",
            Intensity = 0.2,
            DurationMs = 0,
        });

        var v = velocity.ToString(CultureInfo.InvariantCulture);
        var evt = parser.Parse($"FatalCollision Relative Vel: x:{v}, y:0, z:0");

        evt.Should().NotBeNull();
        evt!.Intensity.Should().BeApproximately(expectedIntensity, 1e-9);
    }

    [Fact]
    public void LandingImpactExtractsThreeDimensionalVelocityMagnitude()
    {
        var parser = ParserWith(new EventPattern
        {
            Kind = "LandingImpact",
            Name = "test-landing",
            Pattern = "FatalCollision",
            Intensity = 0.2,
            DurationMs = 0,
        });

        // (3, 4, 12) -> 3D Euclidean norm 13 (a Pythagorean quadruple), unambiguously in the 6..45 range.
        // Distinguishes the correct norm from common wrong extractions: sum=19 -> ~0.567,
        // ignore-z 2D=5 -> 0.35 (floor), max-component=12 -> ~0.45. Only norm=13 yields ~0.467.
        var evt = parser.Parse("FatalCollision Relative Vel: x:3, y:4, z:12");

        // ScaleImpactIntensity(13); the 6/45/0.35 curve constants are independently pinned by test 5's
        // hardcoded boundary rows — here we verify the parser fed the correct 3D magnitude (13) through.
        var expected = 0.35 + (Math.Clamp((13.0 - 6.0) / (45.0 - 6.0), 0, 1) * (1 - 0.35));
        evt.Should().NotBeNull();
        evt!.Intensity.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void NoRouteLoadedQuantumTravelNoiseRequiresBothMarkersToFilter()
    {
        // Pattern matches "Route", so a null return proves the noise filter fired — not that nothing matched.
        var parser = ParserWith(new EventPattern
        {
            Kind = "QuantumSpoolStarted",
            Name = "test-spool",
            Pattern = "Route",
            Intensity = 0.4,
            DurationMs = 0,
        });

        // Both markers present (AND, OrdinalIgnoreCase) -> filtered to null, even though "Route" matches.
        parser.Parse("[QuantumTravel] No Route loaded for jump").Should().BeNull();

        // Only one marker ("No Route loaded", no "[QuantumTravel]") -> filter does NOT fire; the pattern
        // matches and an event is produced. Pins the AND semantics.
        parser.Parse("No Route loaded for jump").Should().NotBeNull();
    }

    [Fact]
    public void FirstMatchingPatternWinsInDeclarationOrder()
    {
        // Both patterns match "shared-token"; the FIRST in declaration order must win (not best-match,
        // not the second). Injecting Kind as a string also exercises the ScEventKind enum parse.
        var parser = ParserWith(
            new EventPattern { Kind = "AtmosphereEntered", Name = "first", Pattern = "shared-token", Intensity = 0.3, DurationMs = 0 },
            new EventPattern { Kind = "AtmosphereExited", Name = "second", Pattern = "shared-token", Intensity = 0.5, DurationMs = 0 });

        var evt = parser.Parse("shared-token present");

        evt.Should().NotBeNull();
        evt!.Kind.Should().Be(ScEventKind.AtmosphereEntered);  // first registered wins
        evt.Name.Should().Be("first");
    }

    [Fact]
    public void LoadDefaultReadsTheVersionedPatternFile()
    {
        // Protects the legacy-path loader: after the v0.json restructure to { schemaVersion, patterns },
        // LoadDefault must still deserialize the deployed file. All 5 patterns load (unsupported ones are
        // still loaded/compiled here; the no-emit behavior lands with the PatternLibrary in step 3).
        StarCitizenEventParser.LoadDefault().PatternCount.Should().Be(5);
    }

    [Fact]
    public void CatastrophicPatternTimesOutAndIsHandledCleanly()
    {
        // ^(a+)+$ against a long non-matching 'a' run is the classic catastrophic-backtracking case.
        // With a short match-timeout the engine throws RegexMatchTimeoutException, which Parse catches
        // and treats as no-match -> finite-time null, not a hang.
        var parser = ParserWith(
            TimeSpan.FromMilliseconds(10),
            new EventPattern { Kind = "QuantumSpoolStarted", Name = "catastrophic", Pattern = "^(a+)+$", Intensity = 0.3, DurationMs = 0 });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var evt = parser.Parse(new string('a', 40) + "b");
        stopwatch.Stop();

        evt.Should().BeNull();                                           // timeout -> no event
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));  // bounded by the 10ms timeout, not 2^40
    }

    [Fact]
    public void UnsupportedPatternIsSkippedDespiteMatching()
    {
        // The first pattern matches the line but is Unsupported -> skipped; the second (supported) wins.
        // Distinguishes "skipped despite matching" from "didn't compile" or "no match" or first-match-wins.
        var parser = ParserWith(
            new EventPattern { Kind = "AtmosphereEntered", Name = "unsupported-first", Pattern = "shared-token", Intensity = 0.3, DurationMs = 0, Unsupported = true },
            new EventPattern { Kind = "AtmosphereExited", Name = "supported-second", Pattern = "shared-token", Intensity = 0.5, DurationMs = 0, Unsupported = false });

        var evt = parser.Parse("shared-token present");

        evt.Should().NotBeNull();
        evt!.Kind.Should().Be(ScEventKind.AtmosphereExited);   // unsupported-first skipped, supported-second wins
        evt.Name.Should().Be("supported-second");
    }
}
