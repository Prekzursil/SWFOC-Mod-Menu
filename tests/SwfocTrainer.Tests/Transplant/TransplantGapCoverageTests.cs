using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Transplant.Services;
using Xunit;

namespace SwfocTrainer.Tests.Transplant;

public sealed class TransplantGapCoverageTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldSkipArtifact_WhenOutputDirectoryMissing()
    {
        var service = new ContentTransplantService(new TransplantCompatibilityService());
        var plan = new TransplantPlan(
            TargetProfileId: "aotr_1397421866_swfoc",
            ActiveWorkshopIds: ["1397421866"],
            Entities:
            [
                new RosterEntityRecord(
                    EntityId: "AOTR_AT_AT",
                    DisplayName: "AT-AT",
                    SourceProfileId: "aotr_1397421866_swfoc",
                    SourceWorkshopId: "1397421866",
                    EntityKind: RosterEntityKind.Unit,
                    DefaultFaction: "Empire",
                    AllowedModes: [RuntimeMode.AnyTactical],
                    VisualRef: "Data/Art/Models/aotr_atat.alo",
                    DependencyRefs: ["Data/XML/Units/AOTR_AT_AT.xml"])
            ]);

        var result = await service.ExecuteAsync(plan, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.ArtifactPath.Should().BeNull();
        result.Diagnostics.Should().ContainKey("allResolved").WhoseValue.Should().Be(true);
    }

    [Fact]
    public async Task ValidateAsync_ShouldTreatWhitespaceWorkshopIds_AsNoTransplantRequired()
    {
        var service = new TransplantCompatibilityService();
        var report = await service.ValidateAsync(
            targetProfileId: "base_swfoc",
            activeWorkshopIds: [" 1125571106 ", "", "1125571106"],
            entities:
            [
                new RosterEntityRecord(
                    EntityId: "EMPIRE_STORMTROOPER_SQUAD",
                    DisplayName: "Stormtrooper Squad",
                    SourceProfileId: "base_swfoc",
                    SourceWorkshopId: " 1125571106 ",
                    EntityKind: RosterEntityKind.Unit,
                    DefaultFaction: "Empire",
                    AllowedModes: [RuntimeMode.AnyTactical],
                    VisualRef: null,
                    DependencyRefs: null)
            ],
            cancellationToken: CancellationToken.None);

        report.AllResolved.Should().BeTrue();
        report.Diagnostics["activeWorkshopIds"].Should().BeAssignableTo<IReadOnlyList<string>>();
        report.Entities.Single().RequiresTransplant.Should().BeFalse();
        report.Entities.Single().ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }
}
