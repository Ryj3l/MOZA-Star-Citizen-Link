using FluentAssertions;
using Moza.ScLink.Core.Devices;

namespace Moza.ScLink.Core.Tests.Devices;

/// <summary>
/// Pins the <see cref="ChainStateChangedEventArgs"/> contract: every property is <c>required</c> and
/// init-only. If a future edit drops <c>required</c> on a property, the compiler does NOT flag every
/// call site — only call sites that omit that property. These tests assert positive construction
/// shape so the type's intended contract is the test surface, not just the per-call-site usage.
/// </summary>
public sealed class ChainStateChangedEventArgsTests
{
    [Fact]
    public void ConstructionWithAllRequiredPropertiesPopulatesAllFields()
    {
        var args = new ChainStateChangedEventArgs
        {
            OutputName = "Auto output (DirectInput: MOZA AB9 FFB Base)",
            OutputStatus = "Using Windows DirectInput force feedback.",
            IsReady = true,
        };

        args.OutputName.Should().Be("Auto output (DirectInput: MOZA AB9 FFB Base)");
        args.OutputStatus.Should().Be("Using Windows DirectInput force feedback.");
        args.IsReady.Should().BeTrue();
    }

    [Fact]
    public void ConstructionWithPreviewTierShapeAndIsReadyFalseIsAccepted()
    {
        var args = new ChainStateChangedEventArgs
        {
            OutputName = "Auto output (Preview output)",
            OutputStatus = "Running without hardware output. Effects are logged for parser validation.",
            IsReady = false,
        };

        args.IsReady.Should().BeFalse();
        args.OutputName.Should().Contain("Preview");
    }

    [Fact]
    public void TypeIsEventArgs()
    {
        // Documents the design choice that the args type extends EventArgs (matches the rest of the
        // codebase's event-args convention, e.g. DeviceStateChangedEventArgs).
        typeof(ChainStateChangedEventArgs).IsAssignableTo(typeof(EventArgs)).Should().BeTrue();
        typeof(ChainStateChangedEventArgs).IsSealed.Should().BeTrue();
    }
}
