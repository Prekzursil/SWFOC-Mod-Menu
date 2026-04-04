using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Fills remaining branch coverage gaps in SelectedUnitTransactionService.
/// Covers: null guards, empty draft, no-effective-change, runtime-mode gates,
/// restore-baseline with no baseline, revert with empty history,
/// convenience overloads, and rollback with partial failures.
/// </summary>
public sealed class SelectedUnitTransactionServiceFullCoverageTests
{
    [Fact]
    public void Ctor_ShouldThrow_WhenRuntimeIsNull()
    {
        var act = () => new SelectedUnitTransactionService(null!, CreateOrchestrator());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenOrchestratorIsNull()
    {
        var act = () => new SelectedUnitTransactionService(new RuntimeStub(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CaptureAsync_ShouldThrow_WhenRuntimeNotAttached()
    {
        var harness = CreateHarness(attached: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.CaptureAsync());
    }

    [Fact]
    public async Task CaptureAsync_ShouldSetBaseline_OnFirstCapture()
    {
        var harness = CreateHarness();
        harness.Service.Baseline.Should().BeNull();

        var snapshot = await harness.Service.CaptureAsync();

        harness.Service.Baseline.Should().NotBeNull();
        snapshot.Hp.Should().Be(120f);
    }

    [Fact]
    public async Task CaptureAsync_ShouldNotOverrideBaseline_OnSubsequentCapture()
    {
        var harness = CreateHarness();
        var first = await harness.Service.CaptureAsync();
        harness.Runtime.SetFloat("selected_hp", 999f);

        var second = await harness.Service.CaptureAsync();

        harness.Service.Baseline!.Hp.Should().Be(first.Hp);
        second.Hp.Should().Be(999f);
    }

    [Fact]
    public async Task CaptureAsync_CancellationOverload_ShouldWork()
    {
        var harness = CreateHarness();
        var snapshot = await harness.Service.CaptureAsync(CancellationToken.None);
        snapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var harness = CreateHarness();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.ApplyAsync(null!, new SelectedUnitDraft(Hp: 100f), RuntimeMode.AnyTactical));
    }

    [Fact]
    public async Task ApplyAsync_ShouldThrow_WhenDraftIsNull()
    {
        var harness = CreateHarness();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.ApplyAsync("profile", null!, RuntimeMode.AnyTactical));
    }

    [Fact]
    public async Task ApplyAsync_ShouldFail_WhenRuntimeModeIsUnknown()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync("profile", new SelectedUnitDraft(Hp: 100f), RuntimeMode.Unknown);
        result.Succeeded.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Diagnostics!["failureReasonCode"].Should().Be("mode_unknown_strict_gate");
    }

    [Fact]
    public async Task ApplyAsync_ShouldFail_WhenRuntimeModeIsGalactic()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync("profile", new SelectedUnitDraft(Hp: 100f), RuntimeMode.Galactic);
        result.Succeeded.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Diagnostics!["failureReasonCode"].Should().Be("mode_mismatch");
    }

    [Fact]
    public async Task ApplyAsync_ShouldFail_WhenDraftIsEmpty()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync(
            "profile", new SelectedUnitDraft(), RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Diagnostics!["failureReasonCode"].Should().Be("empty_draft");
    }

    [Fact]
    public async Task ApplyAsync_ShouldFail_WhenDraftValuesMatchCurrent()
    {
        var harness = CreateHarness();
        // Draft with same values as the runtime stub defaults
        var result = await harness.Service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Hp: 120f, Shield: 60f, Speed: 1.2f,
                DamageMultiplier: 1.0f, CooldownMultiplier: 1.0f, Veterancy: 1, OwnerFaction: 2),
            RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Diagnostics!["failureReasonCode"].Should().Be("no_effective_change");
    }

    [Fact]
    public async Task ApplyAsync_ShouldSucceed_WithTacticalLandMode()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync(
            "test_profile", new SelectedUnitDraft(Hp: 999f), RuntimeMode.TacticalLand);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ShouldSucceed_WithTacticalSpaceMode()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync(
            "test_profile", new SelectedUnitDraft(Shield: 999f), RuntimeMode.TacticalSpace);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ConvenienceOverload_ShouldWork()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync(
            "test_profile", new SelectedUnitDraft(Hp: 500f), RuntimeMode.AnyTactical, CancellationToken.None);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_ShouldApplyIntChanges()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Veterancy: 5, OwnerFaction: 3),
            RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeTrue();
        harness.Runtime.ReadInt("selected_veterancy").Should().Be(5);
        harness.Runtime.ReadInt("selected_owner_faction").Should().Be(3);
    }

    [Fact]
    public async Task ApplyAsync_PartialRollbackFailure_ShouldIndicatePartialRollback()
    {
        var harness = CreateHarness();
        // Fail on shield write, and then also make rollback fail for hp
        harness.Runtime.FailActionId = "set_selected_shield";
        harness.Runtime.FailRollbackActionId = "set_selected_hp";

        var result = await harness.Service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Hp: 333f, Shield: 99f),
            RuntimeMode.AnyTactical);

        result.Succeeded.Should().BeFalse();
        result.RolledBack.Should().BeTrue();
        result.Message.Should().Contain("rollback was partial");
    }

    [Fact]
    public async Task RevertLastAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var harness = CreateHarness();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.RevertLastAsync(null!, RuntimeMode.AnyTactical));
    }

    [Fact]
    public async Task RevertLastAsync_ShouldFail_WhenHistoryIsEmpty()
    {
        var harness = CreateHarness();
        var result = await harness.Service.RevertLastAsync("profile", RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Diagnostics!["failureReasonCode"].Should().Be("history_empty");
    }

    [Fact]
    public async Task RevertLastAsync_ShouldRevertAndRecordMessage_OnSuccess()
    {
        var harness = CreateHarness();
        var applyResult = await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 500f), RuntimeMode.AnyTactical);
        applyResult.Succeeded.Should().BeTrue();
        harness.Service.History.Should().HaveCount(1);
        var originalTransactionId = harness.Service.History[0].TransactionId;

        var result = await harness.Service.RevertLastAsync("test_profile", RuntimeMode.AnyTactical);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Reverted");
        result.Message.Should().Contain(originalTransactionId);
        // RevertLastAsync flow: ApplySnapshotAsync adds a revert entry (count=2),
        // then RemoveAt removes the last entry (the revert), leaving original apply (count=1).
        harness.Service.History.Should().HaveCount(1);
        harness.Service.History[0].TransactionId.Should().Be(originalTransactionId);
    }

    [Fact]
    public async Task RevertLastAsync_ShouldFail_WhenRuntimeModeIsUnknown()
    {
        var harness = CreateHarness();
        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 500f), RuntimeMode.AnyTactical);

        var result = await harness.Service.RevertLastAsync("test_profile", RuntimeMode.Unknown);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RevertLastAsync_ConvenienceOverload_ShouldWork()
    {
        var harness = CreateHarness();
        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 500f), RuntimeMode.AnyTactical);
        var result = await harness.Service.RevertLastAsync("test_profile", RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RevertLastAsync_ShouldReturnNoPendingChanges_WhenSnapshotMatchesCurrent()
    {
        var harness = CreateHarness();
        // Apply a change, then manually revert runtime to match
        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 500f), RuntimeMode.AnyTactical);
        var last = harness.Service.History[^1];
        // Manually set runtime back to what the "before" snapshot was
        harness.Runtime.SetFloat("selected_hp", last.Before.Hp);

        var result = await harness.Service.RevertLastAsync("test_profile", RuntimeMode.AnyTactical);
        // Should succeed with "no pending changes" because current matches before snapshot
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreBaselineAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var harness = CreateHarness();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.RestoreBaselineAsync(null!, RuntimeMode.AnyTactical));
    }

    [Fact]
    public async Task RestoreBaselineAsync_ShouldFail_WhenNoBaselineCaptured()
    {
        var harness = CreateHarness();
        var result = await harness.Service.RestoreBaselineAsync("profile", RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Diagnostics!["failureReasonCode"].Should().Be("baseline_missing");
    }

    [Fact]
    public async Task RestoreBaselineAsync_ShouldFail_WhenRuntimeModeIsUnknown()
    {
        var harness = CreateHarness();
        await harness.Service.CaptureAsync();
        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 999f), RuntimeMode.AnyTactical);

        var result = await harness.Service.RestoreBaselineAsync("test_profile", RuntimeMode.Unknown);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreBaselineAsync_ConvenienceOverload_ShouldWork()
    {
        var harness = CreateHarness();
        await harness.Service.CaptureAsync();
        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 999f), RuntimeMode.AnyTactical);
        var result = await harness.Service.RestoreBaselineAsync("test_profile", RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreBaselineAsync_ShouldFailure_WhenSnapshotApplyFails()
    {
        var harness = CreateHarness();
        await harness.Service.CaptureAsync();
        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 999f), RuntimeMode.AnyTactical);
        harness.Runtime.FailActionId = "set_selected_hp";

        var result = await harness.Service.RestoreBaselineAsync("test_profile", RuntimeMode.AnyTactical);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("failed");
    }

    [Fact]
    public async Task ApplyAsync_ShouldSetBaselineOnFirstApply()
    {
        var harness = CreateHarness();
        harness.Service.Baseline.Should().BeNull();

        await harness.Service.ApplyAsync("test_profile", new SelectedUnitDraft(Hp: 500f), RuntimeMode.AnyTactical);

        harness.Service.Baseline.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyAsync_ShouldApplyAllFieldTypes()
    {
        var harness = CreateHarness();
        var result = await harness.Service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(
                Hp: 999f,
                Shield: 888f,
                Speed: 5.0f,
                DamageMultiplier: 3.0f,
                CooldownMultiplier: 0.5f,
                Veterancy: 10,
                OwnerFaction: 5),
            RuntimeMode.AnyTactical);

        result.Succeeded.Should().BeTrue();
        harness.Runtime.ReadFloat("selected_hp").Should().Be(999f);
        harness.Runtime.ReadFloat("selected_shield").Should().Be(888f);
        harness.Runtime.ReadFloat("selected_speed").Should().Be(5.0f);
        harness.Runtime.ReadFloat("selected_damage_multiplier").Should().Be(3.0f);
        harness.Runtime.ReadFloat("selected_cooldown_multiplier").Should().Be(0.5f);
        harness.Runtime.ReadInt("selected_veterancy").Should().Be(10);
        harness.Runtime.ReadInt("selected_owner_faction").Should().Be(5);
        harness.Service.History.Should().HaveCount(1);
    }

    [Fact]
    public async Task Rollback_ShouldNotExecute_WhenNoChangesWereApplied()
    {
        var harness = CreateHarness();
        // The first action fails immediately, so rollback has zero applied changes
        harness.Runtime.FailActionId = "set_selected_hp";

        var result = await harness.Service.ApplyAsync(
            "test_profile",
            new SelectedUnitDraft(Hp: 999f),
            RuntimeMode.AnyTactical);

        result.Succeeded.Should().BeFalse();
        result.RollbackSteps.Should().BeEmpty();
    }

    private static Harness CreateHarness(bool attached = true)
    {
        var profile = BuildProfile();
        var runtime = new RuntimeStub { IsAttached = attached };
        var repo = new ProfileRepositoryStub(profile);
        var freeze = new FreezeStub();
        var audit = new AuditStub();
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        var service = new SelectedUnitTransactionService(runtime, orchestrator);
        return new Harness(runtime, service);
    }

    private static TrainerOrchestrator CreateOrchestrator()
    {
        var profile = BuildProfile();
        return new TrainerOrchestrator(
            new ProfileRepositoryStub(profile),
            new RuntimeStub(),
            new FreezeStub(),
            new AuditStub());
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
            "test_profile", "Test Profile", null, ExeTarget.Swfoc, null,
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(), actions,
            new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test_schema",
            Array.Empty<HelperHookSpec>());
    }

    private static ActionSpec CreateAction(string id, params string[] required)
    {
        var requiredArray = new JsonArray(required.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        return new ActionSpec(id, ActionCategory.Unit, RuntimeMode.AnyTactical, ExecutionKind.Memory,
            new JsonObject { ["required"] = requiredArray }, VerifyReadback: true, CooldownMs: 0);
    }

    private sealed record Harness(RuntimeStub Runtime, SelectedUnitTransactionService Service);

    internal sealed class RuntimeStub : IRuntimeAdapter
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
        private bool _rollbackFailureConsumed;
        public string? FailActionId { get; set; }
        public string? FailRollbackActionId { get; set; }
        public bool IsAttached { get; set; } = true;
        public AttachSession? CurrentSession => null;

        public void SetFloat(string symbol, float value) => _values[symbol] = value;

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken = default) where T : unmanaged
        {
            var raw = _values[symbol];
            var cast = (T)Convert.ChangeType(raw, typeof(T));
            return Task.FromResult(cast);
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged
        {
            _values[symbol] = value!;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default)
        {
            if (!_failureConsumed && !string.IsNullOrWhiteSpace(FailActionId) &&
                request.Action.Id.Equals(FailActionId, StringComparison.OrdinalIgnoreCase))
            {
                _failureConsumed = true;
                return Task.FromResult(new ActionExecutionResult(false, $"forced failure for {request.Action.Id}", AddressSource.None));
            }

            if (!_rollbackFailureConsumed && !string.IsNullOrWhiteSpace(FailRollbackActionId) &&
                request.Action.Id.Equals(FailRollbackActionId, StringComparison.OrdinalIgnoreCase) &&
                _failureConsumed)
            {
                _rollbackFailureConsumed = true;
                return Task.FromResult(new ActionExecutionResult(false, $"rollback failure for {request.Action.Id}", AddressSource.None));
            }

            var symbol = request.Payload["symbol"]?.GetValue<string>() ?? string.Empty;
            if (request.Payload["floatValue"] is not null)
                _values[symbol] = request.Payload["floatValue"]!.GetValue<float>();
            else if (request.Payload["intValue"] is not null)
                _values[symbol] = request.Payload["intValue"]!.GetValue<int>();

            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        }

        public Task DetachAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public float ReadFloat(string symbol) => Convert.ToSingle(_values[symbol]);
        public int ReadInt(string symbol) => Convert.ToInt32(_values[symbol]);
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
        public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>();
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
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
