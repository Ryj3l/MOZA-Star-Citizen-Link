using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moza.ScLink.Core.Diagnostics;
using Serilog;

namespace Moza.ScLink.Core.Tests.Diagnostics;

public sealed class SerilogConfigurationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public SerilogConfigurationTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void FileSinkCreatesFileWithDailyRollPattern()
    {
        var path = Path.Combine(_tempDir, "app-.log");
        var logger = new LoggerConfiguration()
            .WriteTo.File(path, formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day)
            .CreateLogger();
        logger.Information("test");
        ((IDisposable)logger).Dispose();

        var expected = $"app-{DateTime.Now:yyyyMMdd}.log";
        Directory.GetFiles(_tempDir, "*.log")
            .Select(Path.GetFileName)
            .Should().Contain(expected);
    }

    [Fact]
    public void FileSinkRollsOnSizeCap()
    {
        var path = Path.Combine(_tempDir, "app-.log");
        var logger = new LoggerConfiguration()
            .WriteTo.File(path, formatProvider: CultureInfo.InvariantCulture,
                fileSizeLimitBytes: 200, rollOnFileSizeLimit: true,
                rollingInterval: RollingInterval.Day)
            .CreateLogger();
        for (var i = 0; i < 50; i++)
        {
            logger.Information("padding line {I} with enough text to exceed the 200-byte limit", i);
        }
        ((IDisposable)logger).Dispose();

        Directory.GetFiles(_tempDir, "*.log").Length.Should().BeGreaterThan(1);
    }

    [Fact]
    public void LogRetentionWorkerEnforceRetentionThrowsNotImplemented()
    {
        var worker = new LogRetentionWorker(new TestClock(), new TestFileSystem(), retentionDays: 14);
        var act = () => worker.EnforceRetention(new DirectoryInfo(_tempDir));
        act.Should().Throw<NotImplementedException>();
    }

    [Fact]
    public void ConfigurationOverlayDiagnosticProfileRaisesMinimumLevelToDebug()
    {
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.json"),
            """{"Serilog":{"MinimumLevel":{"Default":"Information"}}}""");
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.diagnostic.json"),
            """{"Serilog":{"MinimumLevel":{"Default":"Debug"}}}""");

        Environment.SetEnvironmentVariable("MOZA_SC_LOG_PROFILE", "diagnostic");
        try
        {
            var profile = Environment.GetEnvironmentVariable("MOZA_SC_LOG_PROFILE") ?? string.Empty;
            var config = new ConfigurationBuilder()
                .SetBasePath(_tempDir)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile($"appsettings.{profile}.json", optional: true)
                .Build();

            config["Serilog:MinimumLevel:Default"].Should().Be("Debug");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOZA_SC_LOG_PROFILE", null);
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class TestFileSystem : IFileSystem
    {
        public IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, string searchPattern) => [];

        public void Delete(FileInfo file) { }
    }
}
