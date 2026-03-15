using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class FileAuditLoggerTests
{
    [Fact]
    public async Task WriteAsync_WithExplicitDirectory_ShouldAppendJsonlRecord()
    {
        var appRoot = TrustedPathPolicy.GetOrCreateAppDataRoot();
        var explicitDirectory = Path.Combine(appRoot, "logs", "tests", $"audit-{Guid.NewGuid():N}");
        var logger = new FileAuditLogger(explicitDirectory);
        var now = DateTimeOffset.UtcNow;
        var record = new ActionAuditRecord(
            Timestamp: now,
            ProfileId: "base_swfoc",
            ProcessId: 4242,
            ActionId: "set_credits",
            AddressSource: AddressSource.Signature,
            Succeeded: true,
            Message: "ok",
            Diagnostics: new Dictionary<string, object?>
            {
                ["reasonCode"] = "CAPABILITY_PROBE_PASS",
                ["value"] = 1337
            });

        await logger.WriteAsync(record, CancellationToken.None);

        var logFilePath = Path.Combine(explicitDirectory, $"audit-{now:yyyy-MM-dd}.jsonl");
        File.Exists(logFilePath).Should().BeTrue();

        var line = (await File.ReadAllLinesAsync(logFilePath)).Last();
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        root.GetProperty("profileId").GetString().Should().Be("base_swfoc");
        root.GetProperty("processId").GetInt32().Should().Be(4242);
        root.GetProperty("actionId").GetString().Should().Be("set_credits");
        root.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        root.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("CAPABILITY_PROBE_PASS");
    }

    [Fact]
    public async Task WriteAsync_WithoutCancellationTokenOverload_ShouldWriteRecord()
    {
        var appRoot = TrustedPathPolicy.GetOrCreateAppDataRoot();
        var explicitDirectory = Path.Combine(appRoot, "logs", "tests", $"audit-overload-{Guid.NewGuid():N}");
        var logger = new FileAuditLogger(explicitDirectory);
        var now = DateTimeOffset.UtcNow;
        var record = new ActionAuditRecord(
            Timestamp: now,
            ProfileId: "roe_3447786229_swfoc",
            ProcessId: 8123,
            ActionId: "spawn_tactical_entity",
            AddressSource: AddressSource.Fallback,
            Succeeded: false,
            Message: "blocked",
            Diagnostics: new Dictionary<string, object?>
            {
                ["reasonCode"] = "MECHANIC_NOT_SUPPORTED_FOR_CHAIN"
            });

        await logger.WriteAsync(record);

        var logFilePath = Path.Combine(explicitDirectory, $"audit-{now:yyyy-MM-dd}.jsonl");
        File.Exists(logFilePath).Should().BeTrue();

        var line = (await File.ReadAllLinesAsync(logFilePath)).Last();
        line.Should().Contain("spawn_tactical_entity");
        line.Should().Contain("MECHANIC_NOT_SUPPORTED_FOR_CHAIN");
    }
}
