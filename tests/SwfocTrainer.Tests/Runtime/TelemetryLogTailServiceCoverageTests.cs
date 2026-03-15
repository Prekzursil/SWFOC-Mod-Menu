#pragma warning disable CA1014
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class TelemetryLogTailServiceCoverageTests
{
    [Fact]
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenNoTelemetryLogExists()
    {
        using var sandbox = new TelemetrySandbox();
        var service = new TelemetryLogTailService();

        var result = service.ResolveLatestMode(sandbox.ProcessPath, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        result.Available.Should().BeFalse();
        result.ReasonCode.Should().Be("telemetry_log_missing");
    }

    [Fact]
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenLogContainsNoTelemetryEntries()
    {
        using var sandbox = new TelemetrySandbox();
        sandbox.WriteLog("_LogFile.txt", "plain text", "still plain text");
        var service = new TelemetryLogTailService();

        var result = service.ResolveLatestMode(sandbox.ProcessPath, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        result.Available.Should().BeFalse();
        result.ReasonCode.Should().Be("telemetry_line_missing");
    }

    [Fact]
    public void ResolveLatestMode_ShouldUseNewestExistingLogCandidate()
    {
        using var sandbox = new TelemetrySandbox();
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-2);
        var later = DateTimeOffset.UtcNow.AddMinutes(-1);
        var underscorePath = sandbox.WriteLog("_LogFile.txt", $"SWFOC_TRAINER_TELEMETRY timestamp={earlier:O} mode=Galactic");
        File.SetLastWriteTimeUtc(underscorePath, earlier.UtcDateTime);
        var plainPath = sandbox.WriteLog("LogFile.txt", $"SWFOC_TRAINER_TELEMETRY timestamp={later:O} mode=Land");
        File.SetLastWriteTimeUtc(plainPath, later.UtcDateTime);
        var service = new TelemetryLogTailService();

        var result = service.ResolveLatestMode(sandbox.ProcessPath, later, TimeSpan.FromMinutes(5));

        result.Available.Should().BeTrue();
        result.Mode.Should().Be(RuntimeMode.TacticalLand);
        result.SourcePath.Should().Be(plainPath);
    }

    [Fact]
    public void ResolveLatestMode_ShouldResolveParentCorruptionLogCandidate()
    {
        using var sandbox = new TelemetrySandbox(processSubdirectory: "GameData");
        var now = DateTimeOffset.UtcNow;
        var corruptionLogPath = sandbox.WriteLog(
            Path.Combine("..", "corruption", "LogFile.txt"),
            $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=Space");
        var service = new TelemetryLogTailService();

        var result = service.ResolveLatestMode(sandbox.ProcessPath, now, TimeSpan.FromMinutes(5));

        result.Available.Should().BeTrue();
        result.Mode.Should().Be(RuntimeMode.TacticalSpace);
        result.SourcePath.Should().Be(corruptionLogPath);
    }

    [Fact]
    public void ResolveLatestMode_ShouldFallbackToLogWriteTime_WhenTimestampCannotBeParsed()
    {
        using var sandbox = new TelemetrySandbox();
        var writeTimeUtc = DateTime.UtcNow;
        var logPath = sandbox.WriteLog("_LogFile.txt", "SWFOC_TRAINER_TELEMETRY timestamp=not-a-date mode=Galactic");
        File.SetLastWriteTimeUtc(logPath, writeTimeUtc);
        var service = new TelemetryLogTailService();

        var result = service.ResolveLatestMode(
            sandbox.ProcessPath,
            new DateTimeOffset(writeTimeUtc.AddSeconds(1), TimeSpan.Zero),
            TimeSpan.FromMinutes(5));

        result.Available.Should().BeTrue();
        result.Mode.Should().Be(RuntimeMode.Galactic);
        result.TimestampUtc.Should().Be(new DateTimeOffset(writeTimeUtc, TimeSpan.Zero));
    }

    [Fact]
    public void ResolveLatestMode_ShouldResetCursor_WhenLogIsTruncatedBetweenReads()
    {
        using var sandbox = new TelemetrySandbox();
        var service = new TelemetryLogTailService();
        var firstNow = DateTimeOffset.UtcNow;
        var logPath = sandbox.WriteLog("_LogFile.txt", $"SWFOC_TRAINER_TELEMETRY timestamp={firstNow:O} mode=Galactic");

        var initial = service.ResolveLatestMode(sandbox.ProcessPath, firstNow, TimeSpan.FromMinutes(5));

        initial.Available.Should().BeTrue();
        initial.Mode.Should().Be(RuntimeMode.Galactic);

        var secondNow = firstNow.AddMinutes(1);
        File.WriteAllText(logPath, $"SWFOC_TRAINER_TELEMETRY timestamp={secondNow:O} mode=Land{Environment.NewLine}");

        var result = service.ResolveLatestMode(sandbox.ProcessPath, secondNow, TimeSpan.FromMinutes(5));

        result.Available.Should().BeTrue();
        result.Mode.Should().Be(RuntimeMode.TacticalLand);
        result.RawLine.Should().Contain("mode=Land");
    }

    [Fact]
    public void ResolveLatestMode_ShouldRescanWholeLog_WhenNoNewTelemetryExistsAfterCursorAdvance()
    {
        using var sandbox = new TelemetrySandbox();
        var now = DateTimeOffset.UtcNow;
        var logPath = sandbox.WriteLog("_LogFile.txt", $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=AnyTactical");
        var service = new TelemetryLogTailService();

        var initial = service.ResolveLatestMode(sandbox.ProcessPath, now, TimeSpan.FromMinutes(5));

        initial.Available.Should().BeTrue();
        File.AppendAllText(logPath, $"noise line{Environment.NewLine}");

        var result = service.ResolveLatestMode(sandbox.ProcessPath, now.AddSeconds(1), TimeSpan.FromMinutes(5));

        result.Available.Should().BeTrue();
        result.Mode.Should().Be(RuntimeMode.AnyTactical);
        result.RawLine.Should().Contain("mode=AnyTactical");
    }

    private sealed class TelemetrySandbox : IDisposable
    {
        private readonly string _root;

        public TelemetrySandbox(string? processSubdirectory = null)
        {
            _root = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-coverage-{Guid.NewGuid():N}");
            var processDirectory = string.IsNullOrWhiteSpace(processSubdirectory)
                ? _root
                : Path.Combine(_root, processSubdirectory);
            Directory.CreateDirectory(processDirectory);
            ProcessPath = Path.Combine(processDirectory, "StarWarsG.exe");
            File.WriteAllText(ProcessPath, string.Empty);
        }

        public string ProcessPath { get; }

        public string WriteLog(string relativePath, params string[] lines)
        {
            var processDirectory = Path.GetDirectoryName(ProcessPath)!;
            var combinedPath = Path.Combine(processDirectory, relativePath);
            var fullPath = Path.GetFullPath(combinedPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
