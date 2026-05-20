using FluentAssertions;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Classification tests for <see cref="DeviceAllowlist"/>. Built from the production
/// <c>device-allowlist.json</c> rule set via <see cref="DeviceAllowlist.FromJson"/> (no file I/O).
/// </summary>
public sealed class DeviceAllowlistTests
{
    // Mirrors src/device-allowlist.json so the tests pin the real production rule set.
    private const string AllowlistJson = """
        {
          "schemaVersion": 1,
          "allowedDeviceModels": [
            {
              "model": "MozaAb6",
              "productNamePatterns": ["MOZA AB6", "AB6 FFB", "AB6"],
              "matchMode": "containsAnyCaseInsensitive"
            },
            {
              "model": "MozaAb9",
              "productNamePatterns": ["MOZA AB9", "AB9 FFB", "AB9"],
              "matchMode": "containsAnyCaseInsensitive"
            }
          ],
          "denylistOverride": []
        }
        """;

    private static DeviceAllowlist AnAllowlist() => DeviceAllowlist.FromJson(AllowlistJson);

    [Theory]
    [InlineData("MOZA AB6 FFB Base", DeviceModel.MozaAb6)]   // positive AB6
    [InlineData("MOZA AB9 Base", DeviceModel.MozaAb9)]       // AB9 -> MozaAb9 (acceptance criterion)
    [InlineData("moza ab6 base", DeviceModel.MozaAb6)]       // case-insensitive
    [InlineData("AB6", DeviceModel.MozaAb6)]                 // partial name
    [InlineData("Logitech G27", DeviceModel.Unknown)]        // negative
    public void ClassifyMapsProductNameToExpectedModel(string productName, DeviceModel expected) =>
        AnAllowlist().Classify(productName).Should().Be(expected);

    [Fact]
    public void ClassifyReturnsUnknownForEmptyProductName() =>
        AnAllowlist().Classify(string.Empty).Should().Be(DeviceModel.Unknown);

    [Fact]
    public void ClassifyReturnsUnknownForNullProductName() =>
        AnAllowlist().Classify(null).Should().Be(DeviceModel.Unknown);

    [Fact]
    public void ClassifyReturnsUnknownWhenRuleHasUnrecognizedMatchMode()
    {
        // A rule with an unrecognized matchMode is dropped (fail-safe); even a literal product-name
        // match then classifies as Unknown rather than being silently driven.
        const string json = """
            {
              "schemaVersion": 1,
              "allowedDeviceModels": [
                { "model": "MozaAb6", "productNamePatterns": ["MOZA AB6"], "matchMode": "regexNotSupported" }
              ]
            }
            """;

        DeviceAllowlist.FromJson(json).Classify("MOZA AB6").Should().Be(DeviceModel.Unknown);
    }
}
