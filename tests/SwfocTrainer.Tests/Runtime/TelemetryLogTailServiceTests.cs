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

    [Fact]
    public void ResolveLatestMode_ShouldFallbackToExistingTelemetry_WhenCursorAlreadyInitialized()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(
            Path.Combine(tempRoot, "_LogFile.txt"),
            $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=Galactic");

        try
        {
            var service = new TelemetryLogTailService();
            service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5)).Available.Should().BeTrue();

            var fallbackResult = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            fallbackResult.Available.Should().BeTrue();
            fallbackResult.Mode.Should().Be(RuntimeMode.Galactic);
            fallbackResult.ReasonCode.Should().Be("telemetry_mode_fresh");
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
    public void ResolveLatestMode_ShouldUseCorruptionLogFallback_WhenPrimaryLogMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "corruption"));
        var processPath = Path.Combine(tempRoot, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        var corruptionLogPath = Path.Combine(tempRoot, "corruption", "LogFile.txt");
        File.WriteAllText(corruptionLogPath, $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=TacticalSpace");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(RuntimeMode.TacticalSpace);
            result.SourcePath.Should().Be(corruptionLogPath);
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
    public void VerifyAutoloadProfile_ShouldReturnReady_WhenFreshAutoloadMarkerExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        File.WriteAllText(
            logPath,
            "SWFOC_TRAINER_HELPER_AUTOLOAD_READY profile=aotr_1397421866_swfoc strategy=story_wrapper_chain script=Library/PGStoryMode.lua");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyAutoloadProfile(
                processPath,
                "aotr_1397421866_swfoc",
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(5));

            result.Ready.Should().BeTrue();
            result.ReasonCode.Should().Be("helper_autoload_ready");
            result.SourcePath.Should().Be(logPath);
            result.Strategy.Should().Be("story_wrapper_chain");
            result.Script.Should().Be("Library/PGStoryMode.lua");
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
    public void ResolveLatestMode_ShouldUseParentCorruptionLogFallback_WhenProcessLivesInChildDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        var processDirectory = Path.Combine(tempRoot, "bin");
        Directory.CreateDirectory(Path.Combine(tempRoot, "corruption"));
        Directory.CreateDirectory(processDirectory);
        var processPath = Path.Combine(processDirectory, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        var fallbackLogPath = Path.Combine(tempRoot, "corruption", "LogFile.txt");
        File.WriteAllText(fallbackLogPath, $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=Land");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(RuntimeMode.TacticalLand);
            result.SourcePath.Should().Be(fallbackLogPath);
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
    public void ResolveLatestMode_ShouldReturnUnavailable_WhenLogExistsButTelemetryLineIsMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), "non-telemetry line");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.ResolveLatestMode(processPath, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Available.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_line_missing");
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
    public void ResolveLatestMode_ShouldReReadTelemetry_WhenLogIsTruncatedAfterCursorInitialization()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        var now = DateTimeOffset.UtcNow;
        File.WriteAllText(logPath, $"SWFOC_TRAINER_TELEMETRY timestamp={now:O} mode=Galactic");

        try
        {
            var service = new TelemetryLogTailService();
            service.ResolveLatestMode(processPath, now, TimeSpan.FromMinutes(5)).Mode.Should().Be(RuntimeMode.Galactic);

            File.WriteAllText(logPath, $"SWFOC_TRAINER_TELEMETRY timestamp={now.AddSeconds(1):O} mode=Space");
            var result = service.ResolveLatestMode(processPath, now.AddSeconds(1), TimeSpan.FromMinutes(5));

            result.Available.Should().BeTrue();
            result.Mode.Should().Be(RuntimeMode.TacticalSpace);
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
    public void VerifyOperationToken_ShouldReturnVerified_WhenAppliedLineExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        File.WriteAllText(logPath, "SWFOC_TRAINER_APPLIED token123 entity=EMP_STORMTROOPER");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyOperationToken(processPath, "token123", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Verified.Should().BeTrue();
            result.ReasonCode.Should().Be("helper_operation_token_verified");
            result.SourcePath.Should().Be(logPath);
            result.RawLine.Should().Contain("SWFOC_TRAINER_APPLIED");
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
    public void VerifyOperationToken_ShouldReturnUnavailable_WhenFailedLineExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), "SWFOC_TRAINER_FAILED token123 entity=EMP_STORMTROOPER");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyOperationToken(processPath, "token123", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Verified.Should().BeFalse();
            result.ReasonCode.Should().Be("helper_operation_reported_failed");
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
    public void VerifyOperationToken_ShouldReturnUnavailable_WhenTokenIsMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "StarWarsG.exe");
        File.WriteAllText(processPath, string.Empty);
        File.WriteAllText(Path.Combine(tempRoot, "_LogFile.txt"), "SWFOC_TRAINER_APPLIED another-token entity=EMP_STORMTROOPER");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyOperationToken(processPath, "token123", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Verified.Should().BeFalse();
            result.ReasonCode.Should().Be("helper_operation_token_not_found");
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
    public void VerifyOperationToken_ShouldReturnUnavailable_WhenTokenArgumentIsMissing()
    {
        var service = new TelemetryLogTailService();

        var result = service.VerifyOperationToken(@"C:\Games\StarWarsG.exe", string.Empty, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        result.Verified.Should().BeFalse();
        result.ReasonCode.Should().Be("helper_operation_token_missing");
    }

    [Fact]
    public void VerifyOperationToken_ShouldReturnUnavailable_WhenProcessPathIsMissing()
    {
        var service = new TelemetryLogTailService();

        var result = service.VerifyOperationToken(null, "token123", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        result.Verified.Should().BeFalse();
        result.ReasonCode.Should().Be("telemetry_process_path_missing");
    }

    [Fact]
    public void VerifyOperationToken_ShouldReturnUnavailable_WhenTokenEvidenceIsStale()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        File.WriteAllText(logPath, "SWFOC_TRAINER_APPLIED stale-token");
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddMinutes(-30));

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyOperationToken(processPath, "stale-token", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Verified.Should().BeFalse();
            result.ReasonCode.Should().Be("helper_operation_token_stale");
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
    public void VerifyOperationToken_ShouldUseParentCorruptionLogFallback_WhenPrimaryLogMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        var processDirectory = Path.Combine(tempRoot, "bin");
        Directory.CreateDirectory(Path.Combine(tempRoot, "corruption"));
        Directory.CreateDirectory(processDirectory);
        var processPath = Path.Combine(processDirectory, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "corruption", "LogFile.txt");
        File.WriteAllText(logPath, "SWFOC_TRAINER_APPLIED token-parent-fallback entity=EMP_STORMTROOPER");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyOperationToken(processPath, "token-parent-fallback", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Verified.Should().BeTrue();
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
    public void VerifyOperationToken_ShouldMatchTokens_IgnoringCase()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        File.WriteAllText(logPath, "swfoc_trainer_applied TOKEN-CASE entity=EMP_STORMTROOPER");

        try
        {
            var service = new TelemetryLogTailService();
            var result = service.VerifyOperationToken(processPath, "token-case", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

            result.Verified.Should().BeTrue();
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
    public void VerifyOperationToken_ShouldFallbackToExistingOperation_WhenCursorAlreadyInitialized()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var processPath = Path.Combine(tempRoot, "swfoc.exe");
        File.WriteAllText(processPath, string.Empty);
        var now = DateTimeOffset.UtcNow;
        var logPath = Path.Combine(tempRoot, "_LogFile.txt");
        File.WriteAllText(logPath, "SWFOC_TRAINER_APPLIED token-reuse entity=EMP_STORMTROOPER");
        File.SetLastWriteTimeUtc(logPath, now.UtcDateTime);

        try
        {
            var service = new TelemetryLogTailService();
            service.VerifyOperationToken(processPath, "token-reuse", now, TimeSpan.FromMinutes(5)).Verified.Should().BeTrue();

            var fallbackResult = service.VerifyOperationToken(processPath, "token-reuse", now, TimeSpan.FromMinutes(5));

            fallbackResult.Verified.Should().BeTrue();
            fallbackResult.ReasonCode.Should().Be("helper_operation_token_verified");
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
