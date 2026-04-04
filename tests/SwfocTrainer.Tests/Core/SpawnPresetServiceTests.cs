using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Tests.Common;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class SpawnPresetServiceTests
{
    [Fact]
    public async Task LoadPresets_ShouldReadProfileScopedPresetFile()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var presetDir = Path.Join(temp.Path, "test_profile");
        Directory.CreateDirectory(presetDir);
        var presetPath = Path.Join(presetDir, "spawn_presets.json");

        var doc = new
        {
            schemaVersion = "1.0",
            presets = new[]
            {
                new
                {
                    id = "stormtrooper_wave",
                    name = "Stormtrooper Wave",
                    unitId = "STORMTROOPER",
                    faction = "EMPIRE",
                    entryMarker = "AUTO",
                    defaultQuantity = 3,
                    defaultDelayMs = 80
                }
            }
        };
        await File.WriteAllTextAsync(presetPath, JsonSerializer.Serialize(doc));

        var presets = await harness.Service.LoadPresetsAsync("test_profile");

        presets.Should().ContainSingle();
        presets[0].Id.Should().Be("stormtrooper_wave");
        presets[0].DefaultQuantity.Should().Be(3);
    }

    [Fact]
    public void BuildBatchPlan_ShouldExpandQuantityDeterministically()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset(
            "id",
            "name",
            "STORMTROOPER",
            "EMPIRE",
            "AUTO",
            DefaultQuantity: 1,
            DefaultDelayMs: 50);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, quantity: 5, delayMs: 120, null, null, stopOnFailure: true);

        plan.Items.Should().HaveCount(5);
        plan.Items[0].Sequence.Should().Be(1);
        plan.Items[4].DelayMs.Should().Be(120);
    }

    [Fact]
    public async Task ExecuteBatch_StopOnFailure_ShouldHaltEarly()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        harness.Runtime.FailOnSequence = 2;
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 0);
        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 4, 0, null, null, stopOnFailure: true);

        var result = await harness.Service.ExecuteBatchAsync("test_profile", plan, RuntimeMode.Galactic);

        result.Succeeded.Should().BeFalse();
        result.StoppedEarly.Should().BeTrue();
        result.Attempted.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteBatch_ContinueOnFailure_ShouldReportPartialFailures()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        harness.Runtime.FailOnSequence = 2;
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 0);
        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 4, 0, null, null, stopOnFailure: false);

        var result = await harness.Service.ExecuteBatchAsync("test_profile", plan, RuntimeMode.Galactic);

        result.Succeeded.Should().BeFalse();
        result.StoppedEarly.Should().BeFalse();
        result.Attempted.Should().Be(4);
        result.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteBatch_UnknownMode_ShouldBlock()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 0);
        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 2, 0, null, null, stopOnFailure: true);

        var result = await harness.Service.ExecuteBatchAsync("test_profile", plan, RuntimeMode.Unknown);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("runtime mode is unknown");
        result.Attempted.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteBatch_EmptyPlan_ShouldReturnSuccess()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var plan = new SpawnBatchPlan("test_profile", "preset_id", false, Array.Empty<SpawnBatchItem>());

        var result = await harness.Service.ExecuteBatchAsync("test_profile", plan, RuntimeMode.Galactic);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("no items");
    }

    [Fact]
    public async Task ExecuteBatch_AllSucceed_ShouldReportFullSuccess()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 0);
        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 3, 0, null, null, stopOnFailure: false);

        var result = await harness.Service.ExecuteBatchAsync("test_profile", plan, RuntimeMode.Galactic);

        result.Succeeded.Should().BeTrue();
        result.SucceededCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.Message.Should().Contain("succeeded");
    }

    [Fact]
    public async Task LoadPresets_ShouldGenerateDefaults_WhenNoPresetFileExists()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);

        var presets = await harness.Service.LoadPresetsAsync("test_profile");

        presets.Should().NotBeEmpty();
        presets[0].Faction.Should().Be("EMPIRE");
    }

    [Fact]
    public async Task LoadPresets_SingleParamOverload_ShouldWork()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);

        var presets = await harness.Service.LoadPresetsAsync("test_profile");

        presets.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildBatchPlan_ShouldUseFactionOverride()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 50);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 2, 100, "REBEL", null, stopOnFailure: false);

        plan.Items.Should().HaveCount(2);
        plan.Items[0].Faction.Should().Be("REBEL");
    }

    [Fact]
    public void BuildBatchPlan_ShouldUseEntryMarkerOverride()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 50);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 1, 50, null, "MARKER_A", stopOnFailure: false);

        plan.Items[0].EntryMarker.Should().Be("MARKER_A");
    }

    [Fact]
    public void BuildBatchPlan_ShouldClampQuantityToMinimum()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 50);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, -5, 50, null, null, stopOnFailure: false);

        plan.Items.Should().ContainSingle();
    }

    [Fact]
    public void BuildBatchPlan_ShouldUsePresetDefaults_WhenQuantityIsZero()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 5, 50);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 0, 50, null, null, stopOnFailure: false);

        plan.Items.Should().HaveCount(5);
    }

    [Fact]
    public void BuildBatchPlan_ShouldUsePresetDefaultDelay_WhenDelayIsNegative()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 200);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 1, -1, null, null, stopOnFailure: false);

        plan.Items[0].DelayMs.Should().Be(200);
    }

    [Fact]
    public async Task LoadPresets_ShouldNormalizeBlankFields()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var presetDir = Path.Join(temp.Path, "test_profile");
        Directory.CreateDirectory(presetDir);
        var doc = new
        {
            schemaVersion = "1.0",
            presets = new[]
            {
                new
                {
                    id = "",
                    name = "",
                    unitId = "AT_ST",
                    faction = "",
                    entryMarker = "",
                    defaultQuantity = -1,
                    defaultDelayMs = -1
                }
            }
        };
        await File.WriteAllTextAsync(Path.Join(presetDir, "spawn_presets.json"), JsonSerializer.Serialize(doc));

        var presets = await harness.Service.LoadPresetsAsync("test_profile");

        presets.Should().ContainSingle();
        presets[0].Id.Should().Be("at_st");
        presets[0].Name.Should().Be("AT_ST");
        presets[0].Faction.Should().Be("EMPIRE");
        presets[0].EntryMarker.Should().Be("AUTO");
        presets[0].DefaultQuantity.Should().BeGreaterThanOrEqualTo(1);
        presets[0].DefaultDelayMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteBatch_MissingSpawnAction_ShouldFail()
    {
        using var temp = new TempDirectory();
        var profile = new TrainerProfile(
            "no_spawn_profile",
            "Test",
            null,
            ExeTarget.Swfoc,
            null,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(),
            "schema",
            Array.Empty<HelperHookSpec>());
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var catalog = new CatalogStub();
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SpawnPresetService(repo, catalog, orchestrator, new LiveOpsOptions { PresetRootPath = temp.Path });

        var plan = new SpawnBatchPlan("no_spawn_profile", "preset_id", false,
            new[] { new SpawnBatchItem(1, "STORMTROOPER", "EMPIRE", "AUTO", 0) });

        var result = await service.ExecuteBatchAsync("no_spawn_profile", plan, RuntimeMode.Galactic);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("does not expose spawn_unit_helper");
    }

    [Fact]
    public async Task LoadPresets_ShouldGenerateDefaultsFromCatalog_WhenNoFactionCatalogExists()
    {
        using var temp = new TempDirectory();
        var profile = BuildProfile();
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var catalogStub = new CatalogStubNoFactions();
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SpawnPresetService(repo, catalogStub, orchestrator, new LiveOpsOptions { PresetRootPath = temp.Path });

        var presets = await service.LoadPresetsAsync("test_profile");

        presets.Should().NotBeEmpty();
        presets[0].Faction.Should().Be("EMPIRE");
    }

    [Fact]
    public async Task LoadPresets_ShouldReturnEmpty_WhenCatalogHasNoUnits()
    {
        using var temp = new TempDirectory();
        var profile = BuildProfile();
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var catalogStub = new CatalogStubEmpty();
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SpawnPresetService(repo, catalogStub, orchestrator, new LiveOpsOptions { PresetRootPath = temp.Path });

        var presets = await service.LoadPresetsAsync("test_profile");

        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPresets_ShouldReturnEmpty_WhenCatalogThrowsIOException()
    {
        using var temp = new TempDirectory();
        var profile = BuildProfile();
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var catalogStub = new CatalogStubThrowsIO();
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SpawnPresetService(repo, catalogStub, orchestrator, new LiveOpsOptions { PresetRootPath = temp.Path });

        var presets = await service.LoadPresetsAsync("test_profile");

        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPresets_ShouldReturnEmpty_WhenCatalogThrowsInvalidOperation()
    {
        using var temp = new TempDirectory();
        var profile = BuildProfile();
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var catalogStub = new CatalogStubThrowsInvalidOp();
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SpawnPresetService(repo, catalogStub, orchestrator, new LiveOpsOptions { PresetRootPath = temp.Path });

        var presets = await service.LoadPresetsAsync("test_profile");

        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteBatch_WithDelay_ShouldStillComplete()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "AUTO", 1, 0);
        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 2, 10, null, null, stopOnFailure: false);

        var result = await harness.Service.ExecuteBatchAsync("test_profile", plan, RuntimeMode.Galactic);

        result.Succeeded.Should().BeTrue();
        result.Attempted.Should().Be(2);
    }

    [Fact]
    public void BuildBatchPlan_ShouldUseWhitespaceDefaultsForOverrides()
    {
        using var temp = new TempDirectory();
        var harness = CreateHarness(temp.Path);
        var preset = new SpawnPreset("id", "name", "STORMTROOPER", "EMPIRE", "MARKER_X", 1, 50);

        var plan = harness.Service.BuildBatchPlan("test_profile", preset, 1, 50, "  ", "  ", stopOnFailure: false);

        plan.Items[0].Faction.Should().Be("EMPIRE");
        plan.Items[0].EntryMarker.Should().Be("MARKER_X");
    }

    private static Harness CreateHarness(string presetRootPath)
    {
        var profile = BuildProfile();
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var catalog = new CatalogStub();
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SpawnPresetService(
            repo,
            catalog,
            orchestrator,
            new LiveOpsOptions { PresetRootPath = presetRootPath });

        return new Harness(service, runtime);
    }

    private static TrainerProfile BuildProfile()
    {
        var spawnAction = new ActionSpec(
            "spawn_unit_helper",
            ActionCategory.Unit,
            RuntimeMode.Unknown,
            ExecutionKind.Helper,
            new JsonObject
            {
                ["required"] = new JsonArray(
                    JsonValue.Create("helperHookId"),
                    JsonValue.Create("unitId"),
                    JsonValue.Create("entryMarker"),
                    JsonValue.Create("faction"))
            },
            VerifyReadback: false,
            CooldownMs: 0);

        return new TrainerProfile(
            "test_profile",
            "Test",
            null,
            ExeTarget.Swfoc,
            null,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["spawn_unit_helper"] = spawnAction
            },
            new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(),
            "schema",
            new[]
            {
                new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0")
            });
    }

    private sealed record Harness(SpawnPresetService Service, RuntimeStub Runtime);

    private sealed class RuntimeStub : IRuntimeAdapter
    {
        private int _counter;

        public int FailOnSequence { get; set; }
        public bool IsAttached => true;
        public AttachSession? CurrentSession => null;

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken = default) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged
        {
            _ = symbol;
            _ = value;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            _counter++;
            if (FailOnSequence > 0 && _counter == FailOnSequence)
            {
                return Task.FromResult(new ActionExecutionResult(false, $"fail:{_counter}", AddressSource.None));
            }

            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        }

        public Task DetachAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CatalogStub : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            IReadOnlyDictionary<string, IReadOnlyList<string>> data = new Dictionary<string, IReadOnlyList<string>>
            {
                ["unit_catalog"] = new[] { "STORMTROOPER", "AT_ST" },
                ["faction_catalog"] = new[] { "EMPIRE" }
            };

            return Task.FromResult(data);
        }
    }

    private sealed class ProfileRepositoryStub : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public ProfileRepositoryStub(TrainerProfile profile) => _profile = profile;

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken = default)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
        }
    }

    private sealed class FreezeStub : IValueFreezeService
    {
        public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>();
        public void FreezeInt(string symbol, int value) { _ = symbol; _ = value; /* no-op stub */ }
        public void FreezeIntAggressive(string symbol, int value) { _ = symbol; _ = value; /* no-op stub */ }
        public void FreezeFloat(string symbol, float value) { _ = symbol; _ = value; /* no-op stub */ }
        public void FreezeBool(string symbol, bool value) { _ = symbol; _ = value; /* no-op stub */ }
        public bool Unfreeze(string symbol) { _ = symbol; return true; }
        public void UnfreezeAll() { /* no-op stub */ }
        public bool IsFrozen(string symbol) { _ = symbol; return false; }
        public void Dispose() { /* no-op stub */ }
    }

    private sealed class AuditStub : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken = default)
        {
            _ = record;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CatalogStubNoFactions : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> data = new Dictionary<string, IReadOnlyList<string>>
            {
                ["unit_catalog"] = new[] { "STORMTROOPER", "AT_ST" }
            };
            return Task.FromResult(data);
        }
    }

    private sealed class CatalogStubEmpty : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> data = new Dictionary<string, IReadOnlyList<string>>
            {
                ["unit_catalog"] = Array.Empty<string>()
            };
            return Task.FromResult(data);
        }
    }

    private sealed class CatalogStubThrowsIO : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken = default)
        {
            throw new IOException("catalog IO failure");
        }
    }

    private sealed class CatalogStubThrowsInvalidOp : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("catalog invalid op");
        }
    }
}
