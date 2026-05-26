using Moza.ScLink.Diagnostics;

namespace Moza.ScLink.App.Tests;

public sealed class ForceFeedbackDiagnosticsTests
{
    [Fact]
    public async Task GetLinesReportsCanonicalDisplayNameAndStateAndOmitsSdkLines()
    {
        var device = new RecordingCanonicalDevice { DisplayName = "MOZA AB9" };
        await device.InitializeAsync(CancellationToken.None);   // -> Ready

        // includeExtendedDiagnostics:false skips the DirectInput hardware enumeration, keeping the test
        // hermetic — it exercises only the canonical device-identity/state rendering + the SDK-line trim.
        var lines = ForceFeedbackDiagnostics.GetLines(device, includeExtendedDiagnostics: false);

        Assert.Contains(lines, l => l.Contains("MOZA AB9", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("Ready", StringComparison.Ordinal));
        Assert.DoesNotContain(
            lines,
            l => l.Contains("MOZA SDK", StringComparison.Ordinal) || l.Contains("bridge", StringComparison.OrdinalIgnoreCase));
    }
}
