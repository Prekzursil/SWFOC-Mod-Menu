using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Transplant.Services;
using Xunit;

namespace SwfocTrainer.Tests.Transplant;

public sealed class TransplantCompatibilityServiceTests
{
    [Fact]
    public async Task ValidateAsync_ShouldPass_WhenEntityBelongsToActiveWorkshopChain()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            new RosterEntityRecord(
                EntityId: "AOTR_AT_AT",
                DisplayName: "AOTR AT-AT",
                SourceProfileId: "aotr_1397421866_swfoc",
                SourceWorkshopId: "1397421866",
                EntityKind: RosterEntityKind.Unit,
                DefaultFaction: "Empire",
                AllowedModes: new[] { RuntimeMode.TacticalLand },
                VisualRef: "Data/Art/Models/aotr_atat.alo",
                DependencyRefs: new[] { "Data/XML/Units/AOTR_AT_AT.xml" })
        };

        var report = await service.ValidateAsync(
            targetProfileId: "aotr_1397421866_swfoc",
            activeWorkshopIds: new[] { "1397421866" },
            entities: entities,
            cancellationToken: CancellationToken.None);

        report.AllResolved.Should().BeTrue();
        report.BlockingEntityCount.Should().Be(0);
        report.Entities.Should().ContainSingle();
        report.Entities[0].Resolved.Should().BeTrue();
        report.Entities[0].ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }

    [Fact]
    public async Task ValidateAsync_ShouldBlockCrossModEntity_WhenVisualAndDependenciesMissing()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            new RosterEntityRecord(
                EntityId: "RAW_MACE_WINDU",
                DisplayName: "Mace Windu",
                SourceProfileId: "raw_1125571106_swfoc",
                SourceWorkshopId: "1125571106",
                EntityKind: RosterEntityKind.Hero,
                DefaultFaction: "Republic",
                AllowedModes: new[] { RuntimeMode.TacticalLand },
                VisualRef: null,
                DependencyRefs: Array.Empty<string>())
        };

        var report = await service.ValidateAsync(
            targetProfileId: "aotr_1397421866_swfoc",
            activeWorkshopIds: new[] { "1397421866" },
            entities: entities,
            cancellationToken: CancellationToken.None);

        report.AllResolved.Should().BeFalse();
        report.BlockingEntityCount.Should().Be(1);
        report.Entities.Should().ContainSingle();
        report.Entities[0].RequiresTransplant.Should().BeTrue();
        report.Entities[0].Resolved.Should().BeFalse();
        report.Entities[0].ReasonCode.Should().Be(RuntimeReasonCode.ROSTER_VISUAL_MISSING);
    }

    [Fact]
    public async Task ValidateAsync_ShouldFlagMissingDependencies_WhenVisualExistsButDepsMissing()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            new RosterEntityRecord(
                EntityId: "RAW_TX130",
                DisplayName: "TX-130",
                SourceProfileId: "raw_1125571106_swfoc",
                SourceWorkshopId: "1125571106",
                EntityKind: RosterEntityKind.Unit,
                DefaultFaction: "Republic",
                AllowedModes: new[] { RuntimeMode.TacticalLand },
                VisualRef: "Data/Art/Models/raw_tx130.alo",
                DependencyRefs: Array.Empty<string>())
        };

        var report = await service.ValidateAsync(
            targetProfileId: "aotr_1397421866_swfoc",
            activeWorkshopIds: new[] { "1397421866" },
            entities: entities,
            cancellationToken: CancellationToken.None);

        report.AllResolved.Should().BeFalse();
        report.BlockingEntityCount.Should().Be(1);
        report.Entities[0].ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_DEPENDENCY_MISSING);
    }
}
