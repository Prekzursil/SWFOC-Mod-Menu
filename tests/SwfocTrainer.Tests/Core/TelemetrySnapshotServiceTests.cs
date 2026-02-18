using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class TelemetrySnapshotServiceTests
{
    [Fact]
    public async Task TelemetrySnapshotService_ShouldRecordAndExportCounters()
    {
        var service = new TelemetrySnapshotService();
        service.RecordAction("set_credits", AddressSource.Signature, succeeded: true);
        service.RecordAction("set_credits", AddressSource.Signature, succeeded: true);
        service.RecordAction("set_selected_hp", AddressSource.Fallback, succeeded: false);

        var snapshot = service.CreateSnapshot();
        snapshot.TotalActions.Should().Be(3);
        snapshot.ActionSuccessCounters["set_credits"].Should().Be(2);
        snapshot.ActionFailureCounters["set_selected_hp"].Should().Be(1);
        snapshot.AddressSourceCounters[AddressSource.Fallback.ToString()].Should().Be(1);
        snapshot.FailureRate.Should().BeApproximately(1d / 3d, 0.0001);
        snapshot.FallbackRate.Should().BeApproximately(1d / 3d, 0.0001);

        var dir = Path.Combine(Path.GetTempPath(), $"swfoc-telemetry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = await service.ExportSnapshotAsync(dir);
            File.Exists(path).Should().BeTrue();
            var json = await File.ReadAllTextAsync(path);
            json.Should().Contain("set_credits");
            json.Should().Contain("TotalActions");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
