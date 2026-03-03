using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Transplant.Services;
using Xunit;

namespace SwfocTrainer.Tests.Transplant;

public sealed class ContentTransplantServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldEmitArtifact_WhenOutputDirectoryProvided()
    {
        var service = new ContentTransplantService(new TransplantCompatibilityService());
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-transplant-{Guid.NewGuid():N}");

        try
        {
            var plan = new TransplantPlan(
                TargetProfileId: "aotr_1397421866_swfoc",
                ActiveWorkshopIds: new[] { "1397421866" },
                Entities: new[]
                {
                    new RosterEntityRecord(
                        EntityId: "AOTR_AT_AT",
                        DisplayName: "AT-AT",
                        SourceProfileId: "aotr_1397421866_swfoc",
                        SourceWorkshopId: "1397421866",
                        EntityKind: RosterEntityKind.Unit,
                        DefaultFaction: "Empire",
                        AllowedModes: new[] { RuntimeMode.AnyTactical },
                        VisualRef: "Data/Art/Models/aotr_atat.alo",
                        DependencyRefs: new[] { "Data/XML/Units/AOTR_AT_AT.xml" })
                },
                OutputDirectory: tempRoot);

            var result = await service.ExecuteAsync(plan, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_APPLIED);
            result.ArtifactPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(result.ArtifactPath!).Should().BeTrue();
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
    public async Task ExecuteAsync_ShouldFail_WhenCompatibilityReportContainsBlockers()
    {
        var service = new ContentTransplantService(new TransplantCompatibilityService());
        var plan = new TransplantPlan(
            TargetProfileId: "aotr_1397421866_swfoc",
            ActiveWorkshopIds: new[] { "1397421866" },
            Entities: new[]
            {
                new RosterEntityRecord(
                    EntityId: "RAW_MACE_WINDU",
                    DisplayName: "Mace Windu",
                    SourceProfileId: "raw_1125571106_swfoc",
                    SourceWorkshopId: "1125571106",
                    EntityKind: RosterEntityKind.Hero,
                    DefaultFaction: "Republic",
                    AllowedModes: new[] { RuntimeMode.AnyTactical },
                    VisualRef: null,
                    DependencyRefs: Array.Empty<string>())
            });

        var result = await service.ExecuteAsync(plan, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED);
        result.Report.BlockingEntityCount.Should().Be(1);
    }
}
