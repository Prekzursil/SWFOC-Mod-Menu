using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
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
        var presetDir = Path.Combine(temp.Path, "test_profile");
        Directory.CreateDirectory(presetDir);
        var presetPath = Path.Combine(presetDir, "spawn_presets.json");

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
                    (JsonNode)JsonValue.Create("helperHookId")!,
                    (JsonNode)JsonValue.Create("unitId")!,
                    (JsonNode)JsonValue.Create("entryMarker")!,
                    (JsonNode)JsonValue.Create("faction")!)
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
            => throw new NotImplementedException();

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken = default) where T : unmanaged
            => throw new NotImplementedException();

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged
            => Task.CompletedTask;

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default)
        {
            _counter++;
            if (FailOnSequence > 0 && _counter == FailOnSequence)
            {
                return Task.FromResult(new ActionExecutionResult(false, $"fail:{_counter}", AddressSource.None));
            }

            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        }

        public Task DetachAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CatalogStub : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken = default)
        {
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
            => throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_profile);

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_profile);

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
    }

    private sealed class FreezeStub : IValueFreezeService
    {
        public IReadOnlyCollection<string> FrozenSymbols => Array.Empty<string>();
        public void FreezeInt(string symbol, int value) { }
        public void FreezeIntAggressive(string symbol, int value) { }
        public void FreezeFloat(string symbol, float value) { }
        public void FreezeBool(string symbol, bool value) { }
        public bool Unfreeze(string symbol) => true;
        public void UnfreezeAll() { }
        public bool IsFrozen(string symbol) => false;
        public void Dispose() { }
    }

    private sealed class AuditStub : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"swfoc-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
