using FluentAssertions;
using Moza.ScLink.Core.Bus;

namespace Moza.ScLink.Core.Tests.Bus;

public sealed class EventBusMetricsTests
{
    [Theory]
    [InlineData(0, 0, 0.01, false)]    // nothing published
    [InlineData(0, 50, 0.01, false)]   // drops but no publications -> no computable rate
    [InlineData(100, 2, 0.01, true)]   // 2% > 1%
    [InlineData(100, 1, 0.01, false)]  // exactly 1% -> not strictly greater
    [InlineData(1000, 5, 0.01, false)] // 0.5%
    [InlineData(100, 50, 0.01, true)]  // 50%
    public void ExceedsDropRateEvaluatesWindowCorrectly(long published, long dropped, double threshold, bool expected) =>
        EventBusMetrics.ExceedsDropRate(published, dropped, threshold).Should().Be(expected);
}
