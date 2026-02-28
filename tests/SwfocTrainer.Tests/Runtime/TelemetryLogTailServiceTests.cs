using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class TelemetryLogTailServiceTests
{
    [Fact]
    public void ResolveLatestMode_ShouldReturnTactical_WhenFreshTelemetryLineExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(logPath, $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=TacticalLand");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(RuntimeMode.Tactical);
            result.ReasonCode.Should().Be("telemetry_mode_fresh");
            result.SourcePath.Should().Be(logPath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenTelemetryIsStale()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var stale = DateTimeOffset.UtcNow.AddMinutes(-10);
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), $"SWFOC_TRAINER_TELEMETRY timestamp={stale:O} mode=Galactic");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

            result.Available.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_stale");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
