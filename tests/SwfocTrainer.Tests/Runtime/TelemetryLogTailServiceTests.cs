using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class TelemetryLogTailServiceTests
{
    [Fact]
    public void ResolveLatestMode_ShouldReturnLand_WhenFreshTelemetryLineExists()
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
            result.Mode.Should().Be(RuntimeMode.TacticalLand);
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

    [Fact]
    public void ResolveLatestMode_ShouldReturnTacticalSpace_WhenSpaceAliasTelemetryLineExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(logPath, $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=Space");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(RuntimeMode.TacticalSpace);
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
    public void ResolveLatestMode_ShouldReturnAnyTactical_WhenAnyTacticalTelemetryLineExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=AnyTactical");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(RuntimeMode.AnyTactical);
            result.ReasonCode.Should().Be("telemetry_mode_fresh");
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
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenProcessPathIsMissing()
    {
        var service = new TelemetryLogTailService();

        var result = service.ResolveLatestMode(null, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        result.Available.Should().BeFalse();
        result.ReasonCode.Should().Be("telemetry_process_path_missing");
    }

    [Fact]
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenTelemetryModeIsUnknown()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=Skirmish");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_mode_unknown");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("mode=Space", RuntimeMode.TacticalSpace)]
    [InlineData("mode=AnyTactical", RuntimeMode.AnyTactical)]
    public void ResolveLatestMode_ShouldParseAdditionalModes(string modeFragment, RuntimeMode expectedMode)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} {modeFragment}");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(expectedMode);
            result.ReasonCode.Should().Be("telemetry_mode_fresh");
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
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenModeIsUnknown()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        File.WriteAllText(
            Path.Combine(tempRoot, "_LogFile.txt"),
            $"SWFOC_TRAINER_TELEMETRY timestamp={DateTimeOffset.UtcNow:O} mode=UnknownMode");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Available.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_mode_unknown");
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
