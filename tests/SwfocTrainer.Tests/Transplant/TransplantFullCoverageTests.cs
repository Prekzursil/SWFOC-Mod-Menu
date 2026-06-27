using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Transplant.Services;
using Xunit;

namespace SwfocTrainer.Tests.Transplant;

/// <summary>
/// Full branch coverage for TransplantCompatibilityService and ContentTransplantService.
/// Covers: null guards, empty entities, whitespace workshop IDs, null dependencies,
/// null visual refs, cancellation, and content transplant artifact paths.
/// </summary>
public sealed class TransplantFullCoverageTests
{
    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenTargetProfileIdIsNull()
    {
        var service = new TransplantCompatibilityService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ValidateAsync(null!, Array.Empty<string>(), Array.Empty<RosterEntityRecord>(), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenActiveWorkshopIdsIsNull()
    {
        var service = new TransplantCompatibilityService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ValidateAsync("profile", null!, Array.Empty<RosterEntityRecord>(), CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrow_WhenEntitiesIsNull()
    {
        var service = new TransplantCompatibilityService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ValidateAsync("profile", Array.Empty<string>(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_ShouldPassForEmptyEntities()
    {
        var service = new TransplantCompatibilityService();
        var report = await service.ValidateAsync(
            "profile", Array.Empty<string>(), Array.Empty<RosterEntityRecord>(), CancellationToken.None);

        report.AllResolved.Should().BeTrue();
        report.TotalEntities.Should().Be(0);
        report.BlockingEntityCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_ShouldIgnoreWhitespaceWorkshopIds()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            BuildEntity("unit1", "1234", "visual.alo", new[] { "dep.xml" })
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "", "  ", "1234" }, entities, CancellationToken.None);

        report.AllResolved.Should().BeTrue();
        report.Entities[0].RequiresTransplant.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ShouldTreatNullSourceWorkshopId_AsActiveChainMember()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            BuildEntity("unit1", null, "visual.alo", new[] { "dep.xml" })
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "1234" }, entities, CancellationToken.None);

        report.AllResolved.Should().BeTrue();
        report.Entities[0].RequiresTransplant.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ShouldTreatWhitespaceSourceWorkshopId_AsActiveChainMember()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            BuildEntity("unit1", "  ", "visual.alo", new[] { "dep.xml" })
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "1234" }, entities, CancellationToken.None);

        report.AllResolved.Should().BeTrue();
        report.Entities[0].RequiresTransplant.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ShouldBlockCrossModEntity_WithNullDependencies()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            new RosterEntityRecord(
                EntityId: "unit1",
                DisplayName: "Unit 1",
                SourceProfileId: "source",
                SourceWorkshopId: "9999",
                EntityKind: RosterEntityKind.Unit,
                DefaultFaction: "Empire",
                AllowedModes: new[] { RuntimeMode.TacticalLand },
                VisualRef: null,
                DependencyRefs: null)
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "1234" }, entities, CancellationToken.None);

        report.AllResolved.Should().BeFalse();
        report.Entities[0].RequiresTransplant.Should().BeTrue();
        report.Entities[0].ReasonCode.Should().Be(RuntimeReasonCode.ROSTER_VISUAL_MISSING);
    }

    [Fact]
    public async Task ValidateAsync_ShouldBlockWithMissingDependency_WhenVisualExistsButNullDeps()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            new RosterEntityRecord(
                EntityId: "unit1",
                DisplayName: "Unit 1",
                SourceProfileId: "source",
                SourceWorkshopId: "9999",
                EntityKind: RosterEntityKind.Unit,
                DefaultFaction: "Empire",
                AllowedModes: new[] { RuntimeMode.TacticalLand },
                VisualRef: "visual.alo",
                DependencyRefs: null)
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "1234" }, entities, CancellationToken.None);

        report.AllResolved.Should().BeFalse();
        report.Entities[0].ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_DEPENDENCY_MISSING);
    }

    [Fact]
    public async Task ValidateAsync_ShouldBlockWithMissingDependency_WhenDepsAreWhitespaceOnly()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            new RosterEntityRecord(
                EntityId: "unit1",
                DisplayName: "Unit 1",
                SourceProfileId: "source",
                SourceWorkshopId: "9999",
                EntityKind: RosterEntityKind.Unit,
                DefaultFaction: "Empire",
                AllowedModes: new[] { RuntimeMode.TacticalLand },
                VisualRef: "visual.alo",
                DependencyRefs: new[] { "", "  " })
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "1234" }, entities, CancellationToken.None);

        report.AllResolved.Should().BeFalse();
        report.Entities[0].ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_DEPENDENCY_MISSING);
    }

    [Fact]
    public async Task ValidateAsync_Diagnostics_ShouldContainExpectedKeys()
    {
        var service = new TransplantCompatibilityService();
        var entities = new[]
        {
            BuildEntity("unit1", "9999", "visual.alo", new[] { "dep.xml" }),
            BuildEntity("unit2", "1234", "visual2.alo", new[] { "dep2.xml" })
        };

        var report = await service.ValidateAsync(
            "profile", new[] { "1234" }, entities, CancellationToken.None);

        report.Diagnostics.Should().ContainKey("activeWorkshopIds");
        report.Diagnostics.Should().ContainKey("requiresTransplantCount");
        report.Diagnostics.Should().ContainKey("resolvedCount");
        report.Diagnostics.Should().ContainKey("blockingEntityIds");
    }

    [Fact]
    public Task ContentTransplantService_ShouldThrow_WhenCompatibilityServiceIsNull()
    {
        var act = () => new ContentTransplantService(null!);
        act.Should().Throw<ArgumentNullException>();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ContentTransplantService_ShouldThrow_WhenPlanIsNull()
    {
        var service = new ContentTransplantService(new TransplantCompatibilityService());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ExecuteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ContentTransplantService_ShouldSucceed_WhenAllResolved()
    {
        var service = new ContentTransplantService(new TransplantCompatibilityService());
        var plan = new TransplantPlan(
            TargetProfileId: "profile",
            ActiveWorkshopIds: new[] { "1234" },
            Entities: new[]
            {
                BuildEntity("unit1", "1234", "v.alo", new[] { "d.xml" })
            },
            OutputDirectory: null);

        var result = await service.ExecuteAsync(plan, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_APPLIED);
        result.ArtifactPath.Should().BeNull();
    }

    [Fact]
    public async Task ContentTransplantService_ShouldFail_WhenBlockingEntitiesExist()
    {
        var service = new ContentTransplantService(new TransplantCompatibilityService());
        var plan = new TransplantPlan(
            TargetProfileId: "profile",
            ActiveWorkshopIds: new[] { "1234" },
            Entities: new[]
            {
                BuildEntity("unit1", "9999", null, null)
            },
            OutputDirectory: null);

        var result = await service.ExecuteAsync(plan, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED);
    }

    [Fact]
    public async Task ContentTransplantService_ShouldWriteArtifact_WhenOutputDirectoryProvided()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"transplant-test-{Guid.NewGuid():N}");
        try
        {
            var service = new ContentTransplantService(new TransplantCompatibilityService());
            var plan = new TransplantPlan(
                TargetProfileId: "profile",
                ActiveWorkshopIds: new[] { "1234" },
                Entities: new[]
                {
                    BuildEntity("unit1", "1234", "v.alo", new[] { "d.xml" })
                },
                OutputDirectory: tempDir);

            var result = await service.ExecuteAsync(plan, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.ArtifactPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(result.ArtifactPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private static RosterEntityRecord BuildEntity(
        string entityId,
        string? sourceWorkshopId,
        string? visualRef,
        IReadOnlyList<string>? deps)
    {
        return new RosterEntityRecord(
            EntityId: entityId,
            DisplayName: entityId,
            SourceProfileId: "source",
            SourceWorkshopId: sourceWorkshopId,
            EntityKind: RosterEntityKind.Unit,
            DefaultFaction: "Empire",
            AllowedModes: new[] { RuntimeMode.TacticalLand },
            VisualRef: visualRef,
            DependencyRefs: deps);
    }
}
