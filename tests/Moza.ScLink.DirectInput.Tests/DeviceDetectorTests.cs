using FluentAssertions;
using Moza.ScLink.Core.Models;
using NSubstitute;

namespace Moza.ScLink.DirectInput.Tests;

/// <summary>
/// Tests for <see cref="DeviceDetector"/>: it enumerates FFB devices via
/// <see cref="IDirectInputAbstraction"/>, classifies each against a <see cref="DeviceAllowlist"/>, and
/// returns every device — including <see cref="DeviceModel.Unknown"/> ones — so diagnostics can list them.
/// </summary>
public sealed class DeviceDetectorTests
{
    private const string AllowlistJson = """
        {
          "schemaVersion": 1,
          "allowedDeviceModels": [
            { "model": "MozaAb6", "productNamePatterns": ["AB6"], "matchMode": "containsAnyCaseInsensitive" },
            { "model": "MozaAb9", "productNamePatterns": ["AB9"], "matchMode": "containsAnyCaseInsensitive" }
          ]
        }
        """;

    [Fact]
    public void DetectForceFeedbackDevicesClassifiesEachAndKeepsUnknownDevices()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();
        var ab9 = new DirectInputDeviceInfo(Guid.NewGuid(), "MOZA AB9 Base", "MOZA AB9 Base");
        var g27 = new DirectInputDeviceInfo(Guid.NewGuid(), "Logitech G27", "Logitech G27");
        abstraction.EnumerateForceFeedbackDevices().Returns(new[] { ab9, g27 });

        var detector = new DeviceDetector(abstraction, DeviceAllowlist.FromJson(AllowlistJson));

        var detected = detector.DetectForceFeedbackDevices();

        detected.Should().HaveCount(2);
        detected.Should().ContainSingle(d => d.Info.ProductName == "MOZA AB9 Base" && d.Model == DeviceModel.MozaAb9);
        detected.Should().ContainSingle(d => d.Info.ProductName == "Logitech G27" && d.Model == DeviceModel.Unknown);
    }

    [Fact]
    public void ConstructorThrowsForNullAbstraction()
    {
        var construct = () => new DeviceDetector(abstraction: null!, DeviceAllowlist.FromJson(AllowlistJson));

        construct.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("abstraction");
    }

    [Fact]
    public void ConstructorThrowsForNullAllowlist()
    {
        var abstraction = Substitute.For<IDirectInputAbstraction>();

        var construct = () => new DeviceDetector(abstraction, allowlist: null!);

        construct.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("allowlist");
    }
}
