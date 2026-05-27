using Moza.ScLink.App.ForceFeedback;

namespace Moza.ScLink.App.Tests;

/// <summary>
/// Covers <see cref="ForceFeedbackDeviceFactory.CreateCanonical(bool)"/> (T-17 forced-preview wiring).
/// Only the <c>forcePreview: true</c> path is deterministic and hardware-independent — it short-circuits
/// DirectInput enumeration and returns the preview device. The <c>forcePreview: false</c> path is
/// hardware-dependent by design (returns a real <c>VorticeDirectInputDevice</c> when an allowlisted base
/// enumerates, else the preview device), so it is not type-asserted here: doing so would require either a
/// MOZA base attached or a real COM enumeration in a unit test. The factory keeps no injectable
/// enumeration seam (static + Profiles-free per T-27), and the no-hardware fallback is exercised
/// end-to-end by the host integration tests.
/// </summary>
public sealed class ForceFeedbackDeviceFactoryTests
{
    [Fact]
    public async Task CreateCanonicalWithForcePreviewReturnsPreviewDevice()
    {
        // forcePreview short-circuits enumeration regardless of attached hardware → deterministic.
        await using var device = ForceFeedbackDeviceFactory.CreateCanonical(forcePreview: true);

        Assert.IsType<PreviewForceFeedbackDevice>(device);
    }
}
