using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class SelectedUnitTransactionServiceTests
{
    [Fact]
    public async Task Apply_AllWritesSuccessful_ShouldCommitHistory()
    {
        var harness = CreateHarness();
        var service = harness.Service;

        var result = await service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Hp: 240f, Shield: 80f),
            RuntimeMode.AnyTactical);

        result.Succeeded.Should().BeTrue();
        service.History.Should().HaveCount(1);
        harness.Runtime.ReadFloat("selected_hp").Should().Be(240f);
        harness.Runtime.ReadFloat("selected_shield").Should().Be(80f);
    }

    [Fact]
    public async Task Apply_MidStreamFailure_ShouldRollbackAppliedWrites()
    {
        var harness = CreateHarness();
        harness.Runtime.FailActionId = "set_selected_shield";
        var service = harness.Service;

        var beforeHp = harness.Runtime.ReadFloat("selected_hp");
        var beforeShield = harness.Runtime.ReadFloat("selected_shield");

        var result = await service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Hp: 333f, Shield: 99f),
            RuntimeMode.AnyTactical);

        result.Succeeded.Should().BeFalse();
        result.RolledBack.Should().BeTrue();
        harness.Runtime.ReadFloat("selected_hp").Should().Be(beforeHp);
        harness.Runtime.ReadFloat("selected_shield").Should().Be(beforeShield);
    }

    [Fact]
    public async Task RevertLast_ShouldRestorePreviousSnapshot()
    {
        var harness = CreateHarness();
        var service = harness.Service;

        var baseline = await service.CaptureAsync();
        await service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Hp: baseline.Hp + 100f, Veterancy: baseline.Veterancy + 2),
            RuntimeMode.AnyTactical);

        var revert = await service.RevertLastAsync("test_profile", RuntimeMode.AnyTactical);

        revert.Succeeded.Should().BeTrue();
        harness.Runtime.ReadFloat("selected_hp").Should().BeApproximately(baseline.Hp, 0.001f);
        harness.Runtime.ReadInt("selected_veterancy").Should().Be(baseline.Veterancy);
    }

    [Fact]
    public async Task RestoreBaseline_AfterMultipleTransactions_ShouldReturnBaseline()
    {
        var harness = CreateHarness();
        var service = harness.Service;

        var baseline = await service.CaptureAsync();
        await service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: baseline.Hp + 25f), RuntimeMode.AnyTactical);
        await service.ApplyAsync("test_profile", new SelectedUnitDraft(Veterancy: baseline.Veterancy + 3), RuntimeMode.AnyTactical);

        var restored = await service.RestoreBaselineAsync("test_profile", RuntimeMode.AnyTactical);

        restored.Succeeded.Should().BeTrue();
        harness.Runtime.ReadFloat("selected_hp").Should().BeApproximately(baseline.Hp, 0.001f);
        harness.Runtime.ReadInt("selected_veterancy").Should().Be(baseline.Veterancy);
    }

    private static Harness CreateHarness()
    {
        var profile = BuildProfile();
        var runtime = new RuntimeStub();
        var repo = new ProfileRepositoryStub(profile);
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SelectedUnitTransactionService(runtime, orchestrator);
        return new Harness(runtime, service);
    }

    private static TrainerProfile BuildProfile()
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_selected_hp"] = CreateAction("set_selected_hp", "symbol", "floatValue"),
            ["set_selected_shield"] = CreateAction("set_selected_shield", "symbol", "floatValue"),
            ["set_selected_speed"] = CreateAction("set_selected_speed", "symbol", "floatValue"),
            ["set_selected_damage_multiplier"] = CreateAction("set_selected_damage_multiplier", "symbol", "floatValue"),
            ["set_selected_cooldown_multiplier"] = CreateAction("set_selected_cooldown_multiplier", "symbol", "floatValue"),
            ["set_selected_veterancy"] = CreateAction("set_selected_veterancy", "symbol", "intValue"),
            ["set_selected_owner_faction"] = CreateAction("set_selected_owner_faction", "symbol", "intValue"),
        };

        return new TrainerProfile(
            "test_profile",
            "Test Profile",
            null,
            ExeTarget.Swfoc,
            null,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            actions,
            new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(),
            "test_schema",
            Array.Empty<HelperHookSpec>());
    }

    private static ActionSpec CreateAction(string id, params string[] required)
    {
        var requiredArray = new JsonArray(required.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        return new ActionSpec(
            id,
            ActionCategory.Unit,
            RuntimeMode.AnyTactical,
            ExecutionKind.Memory,
            new JsonObject { ["required"] = requiredArray },
            VerifyReadback: true,
            CooldownMs: 0);
    }

    private sealed record Harness(RuntimeStub Runtime, SelectedUnitTransactionService Service);

    private sealed class RuntimeStub : IRuntimeAdapter
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["selected_hp"] = 120f,
            ["selected_shield"] = 60f,
            ["selected_speed"] = 1.2f,
            ["selected_damage_multiplier"] = 1.0f,
            ["selected_cooldown_multiplier"] = 1.0f,
            ["selected_veterancy"] = 1,
            ["selected_owner_faction"] = 2,
        };

        private bool _failureConsumed;
        public string? FailActionId { get; set; }

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
            _ = cancellationToken;
            var raw = _values[symbol];
            var cast = (T)Convert.ChangeType(raw, typeof(T));
            return Task.FromResult(cast);
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged
        {
            _ = cancellationToken;
            _values[symbol] = value!;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            if (!_failureConsumed && !string.IsNullOrWhiteSpace(FailActionId) &&
                request.Action.Id.Equals(FailActionId, StringComparison.OrdinalIgnoreCase))
            {
                _failureConsumed = true;
                return Task.FromResult(new ActionExecutionResult(false, $"forced failure for {request.Action.Id}", AddressSource.None));
            }

            var symbol = request.Payload["symbol"]?.GetValue<string>() ?? string.Empty;
            if (request.Payload["floatValue"] is not null)
            {
                _values[symbol] = request.Payload["floatValue"]!.GetValue<float>();
            }
            else if (request.Payload["intValue"] is not null)
            {
                _values[symbol] = request.Payload["intValue"]!.GetValue<int>();
            }

            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        }

        public Task DetachAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public float ReadFloat(string symbol) => Convert.ToSingle(_values[symbol]);

        public int ReadInt(string symbol) => Convert.ToInt32(_values[symbol]);
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
}
