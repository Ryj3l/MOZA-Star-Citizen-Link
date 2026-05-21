using System.Globalization;
using System.IO;
using FluentAssertions;
using Moza.ScLink.Effects.Catalogs;

namespace Moza.ScLink.Effects.Tests;

public sealed class EffectCatalogTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"moza-catalog-test-{Guid.NewGuid():N}.json");

    private static readonly TimeSpan FastDebounce = TimeSpan.FromMilliseconds(100);

    // Builds one valid effect row; each parameter overrides a single field so a test can mutate exactly
    // one value into an invalid state. Doubles are formatted InvariantCulture so a comma-decimal locale
    // never produces malformed JSON.
    private static string Effect(
        string id = "e1",
        string category = "Flight",
        string effectType = "Periodic",
        double baseIntensity = 0.5,
        double frequencyHz = 30,
        int durationMs = 100,
        string envelope = "null")
    {
        var bi = baseIntensity.ToString(CultureInfo.InvariantCulture);
        var fz = frequencyHz.ToString(CultureInfo.InvariantCulture);
        return $$"""
            {
              "effectId": "{{id}}",
              "displayName": "display",
              "category": "{{category}}",
              "effectType": "{{effectType}}",
              "baseIntensity": {{bi}},
              "frequencyHz": {{fz}},
              "durationMs": {{durationMs}},
              "directionX": 0.0,
              "directionY": 1.0,
              "envelope": {{envelope}},
              "isSustained": false,
              "stateKey": null,
              "stoppedBy": [],
              "notes": null
            }
            """;
    }

    private static string Catalog(int schemaVersion, params string[] effects) =>
        $$"""
        { "schemaVersion": {{schemaVersion}}, "catalogId": "test", "effects": [ {{string.Join(",", effects)}} ] }
        """;

    private static void Write(string path, string content)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(fs);
        writer.Write(content);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
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

    [Fact]
    public void ShippedPhase1CatalogLoadsAllSevenEffects()
    {
        // Validates the DEPLOYED Catalogs/phase1.json is internally consistent (all 7 pass validation),
        // not just the loading mechanism. A future bad edit (e.g. baseIntensity 1.5) trips this.
        using var catalog = EffectCatalog.LoadDefault();

        catalog.Current.Should().HaveCount(7);
    }

    [Fact]
    public void HappyPathLoadsAndExposesEffects()
    {
        var env = """{ "attackMs": 10, "holdMs": 20, "decayMs": 30, "releaseMs": 40, "attackLevel": 0.5, "sustainLevel": 0.9 }""";
        Write(_path, Catalog(1, Effect(id: "a", envelope: env), Effect(id: "b")));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);

        catalog.Current.Should().HaveCount(2);
        var a = catalog.Current.Single(e => e.EffectId == "a");
        a.Envelope.Should().NotBeNull();
        a.Envelope!.AttackMs.Should().Be(10);
        a.Envelope.SustainLevel.Should().Be(0.9);
    }

    [Fact]
    public void SchemaVersionMismatchYieldsEmptyCatalog()
    {
        Write(_path, Catalog(2, Effect()));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);

        catalog.Current.Should().BeEmpty();
    }

    [Fact]
    public void MissingSchemaVersionYieldsEmptyCatalog()
    {
        // No schemaVersion -> deserializes to default 0 (!= 1) -> "load defaults" (empty).
        Write(_path, $$"""{ "catalogId": "test", "effects": [ {{Effect()}} ] }""");
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);

        catalog.Current.Should().BeEmpty();
    }

    [Fact]
    public void NullDocumentYieldsEmptyCatalog()
    {
        Write(_path, "null");
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);

        catalog.Current.Should().BeEmpty();
    }

    [Fact]
    public void MissingFileYieldsEmptyCatalog()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"moza-catalog-missing-{Guid.NewGuid():N}.json");
        using var catalog = new EffectCatalog(missing, debounceWindow: FastDebounce);

        catalog.Current.Should().BeEmpty();
    }

    [Fact]
    public void OutOfRangeIntensityEffectRejectedOthersSurvive()
    {
        Write(_path, Catalog(1, Effect(id: "good", baseIntensity: 0.5), Effect(id: "bad", baseIntensity: 1.5)));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);

        // Distinguishing claim: the VALID effect survives and the invalid one is dropped — not
        // "dropped nothing" (count 2) and not "dropped the wrong one".
        catalog.Current.Should().ContainSingle();
        catalog.Current[0].EffectId.Should().Be("good");
    }

    [Theory]
    [InlineData("emptyId")]
    [InlineData("badCategory")]
    [InlineData("badEffectType")]
    [InlineData("intensityHigh")]
    [InlineData("intensityNegative")]
    [InlineData("frequencyNegative")]
    [InlineData("durationNegative")]
    public void InvalidEffectIsRejected(string variant)
    {
        var effect = variant switch
        {
            "emptyId" => Effect(id: ""),
            "badCategory" => Effect(category: "Nonsense"),
            "badEffectType" => Effect(effectType: "Nonsense"),
            "intensityHigh" => Effect(baseIntensity: 1.5),
            "intensityNegative" => Effect(baseIntensity: -0.1),
            "frequencyNegative" => Effect(frequencyHz: -1),
            "durationNegative" => Effect(durationMs: -1),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        Write(_path, Catalog(1, effect));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);

        catalog.Current.Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedReloadRetainsPriorAndDoesNotSignalChange()
    {
        Write(_path, Catalog(1, Effect(id: "x")));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);
        var changedCount = 0;
        catalog.Changed += (_, _) => Interlocked.Increment(ref changedCount);

        catalog.Current.Should().ContainSingle();

        Write(_path, "{ not valid json");
        await Task.Delay(400);  // debounce window + reload attempt

        catalog.Current.Should().ContainSingle();          // (a) prior retained, not degraded
        catalog.Current[0].EffectId.Should().Be("x");
        changedCount.Should().Be(0);                        // (b) no misleading change signal
    }

    [Fact]
    public async Task HotReloadPicksUpChangesWithinOneSecond()
    {
        Write(_path, Catalog(1, Effect(id: "first")));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);
        catalog.Current[0].EffectId.Should().Be("first");

        Write(_path, Catalog(1, Effect(id: "second")));
        await WaitUntilAsync(() => catalog.Current.Count > 0 && catalog.Current[0].EffectId == "second", TimeSpan.FromSeconds(1));

        catalog.Current[0].EffectId.Should().Be("second");
    }

    [Fact]
    public async Task RapidWritesDebounceToASingleReload()
    {
        Write(_path, Catalog(1, Effect(id: "x")));
        using var catalog = new EffectCatalog(_path, debounceWindow: FastDebounce);
        var reloadCount = 0;
        catalog.Changed += (_, _) => Interlocked.Increment(ref reloadCount);

        // Five rapid rewrites within the debounce window must coalesce into exactly one reload.
        for (var i = 0; i < 5; i++)
        {
            Write(_path, Catalog(1, Effect(id: "y")));
        }

        await WaitUntilAsync(() => reloadCount >= 1, TimeSpan.FromSeconds(1));
        await Task.Delay(300);  // let any trailing (coalesced-away) reload settle

        reloadCount.Should().Be(1);  // coalesced, not 5
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
