using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Unit tests for <see cref="TrainerOrchestrator"/>, focusing on freeze-action dispatch,
/// payload validation, and mode gating. Uses lightweight stubs (no mocking library).
/// </summary>
public sealed class TrainerOrchestratorTests
{
    #region ── Stubs ───────────────────────────────────────────────────────

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public StubProfileRepository(TrainerProfile profile) => _profile = profile;

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken ct = default)
            => Task.FromResult(_profile);

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken ct = default)
            => Task.FromResult(_profile);

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
    }

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached => true;
        public AttachSession? CurrentSession => null;

        public List<ActionExecutionRequest> ReceivedRequests { get; } = new();

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct = default) where T : unmanaged
            => throw new NotImplementedException();

        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct = default) where T : unmanaged
            => Task.CompletedTask;

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct = default)
        {
            ReceivedRequests.Add(request);
            return Task.FromResult(new ActionExecutionResult(true, "stub OK", AddressSource.None));
        }

        public Task DetachAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Records all freeze/unfreeze calls for assertion.</summary>
    private sealed class RecordingFreezeService : IValueFreezeService
    {
        public List<(string Symbol, string Type, object Value)> FrozenCalls { get; } = new();
        public List<string> UnfrozenCalls { get; } = new();
        public bool UnfreezeAllCalled { get; private set; }
        public IReadOnlyCollection<string> FrozenSymbols => FrozenCalls.Select(c => c.Symbol).Distinct().ToList();

        public void FreezeInt(string symbol, int value) => FrozenCalls.Add((symbol, "int", value));
        public void FreezeIntAggressive(string symbol, int value) => FrozenCalls.Add((symbol, "int_aggressive", value));
        public void FreezeFloat(string symbol, float value) => FrozenCalls.Add((symbol, "float", value));
        public void FreezeBool(string symbol, bool value) => FrozenCalls.Add((symbol, "bool", value));

        public bool Unfreeze(string symbol)
        {
            UnfrozenCalls.Add(symbol);
            return true;
        }

        public void UnfreezeAll() => UnfreezeAllCalled = true;
        public bool IsFrozen(string symbol) => FrozenCalls.Any(c => c.Symbol == symbol);
        public void Dispose() { }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public List<ActionAuditRecord> Records { get; } = new();

        public Task WriteAsync(ActionAuditRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region ── Helpers ─────────────────────────────────────────────────────

    private static ActionSpec MakeFreezeAction(string id = "freeze_symbol", params string[] required)
    {
        var requiredArray = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return new ActionSpec(
            Id: id,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Freeze,
            PayloadSchema: new JsonObject { ["required"] = requiredArray },
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "test freeze action");
    }

    private static ActionSpec MakeMemoryAction(string id = "set_credits", RuntimeMode mode = RuntimeMode.Unknown, params string[] required)
    {
        var requiredArray = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return new ActionSpec(
            Id: id,
            Category: ActionCategory.Economy,
            Mode: mode,
            ExecutionKind: ExecutionKind.Memory,
            PayloadSchema: new JsonObject { ["required"] = requiredArray },
            VerifyReadback: true,
            CooldownMs: 100,
            Description: "test memory action");
    }

    private static ActionSpec MakeCodePatchAction(string id = "set_unit_cap", params string[] required)
    {
        var requiredArray = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return new ActionSpec(
            Id: id,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.CodePatch,
            PayloadSchema: new JsonObject { ["required"] = requiredArray },
            VerifyReadback: false,
            CooldownMs: 200,
            Description: "test code-patch action");
    }

    private static TrainerProfile BuildProfile(params ActionSpec[] actions)
    {
        var dict = actions.ToDictionary(a => a.Id);
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test Profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: dict,
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "",
            HelperModHooks: Array.Empty<HelperHookSpec>());
    }

    private (TrainerOrchestrator Orchestrator, StubRuntimeAdapter Runtime, RecordingFreezeService Freeze, StubAuditLogger Audit)
        CreateOrchestrator(TrainerProfile profile)
    {
        var runtime = new StubRuntimeAdapter();
        var freeze = new RecordingFreezeService();
        var audit = new StubAuditLogger();
        var repo = new StubProfileRepository(profile);
        var orchestrator = new TrainerOrchestrator(repo, runtime, freeze, audit);
        return (orchestrator, runtime, freeze, audit);
    }

    #endregion

    // ─── Freeze action dispatch ─────────────────────────────────────────

    [Fact]
    public async Task FreezeAction_With_IntValue_Should_Call_FreezeInt()
    {
        var profile = BuildProfile(MakeFreezeAction("freeze_symbol", "symbol", "freeze"));
        var (orchestrator, _, freeze, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true,
            ["intValue"] = 999999
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "freeze_symbol", payload, RuntimeMode.Galactic);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("credits");
        freeze.FrozenCalls.Should().ContainSingle()
            .Which.Should().Be(("credits", "int", 999999));
    }

    [Fact]
    public async Task FreezeAction_With_FloatValue_Should_Call_FreezeFloat()
    {
        var profile = BuildProfile(MakeFreezeAction("freeze_symbol", "symbol", "freeze"));
        var (orchestrator, _, freeze, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "game_speed",
            ["freeze"] = true,
            ["floatValue"] = 2.5f
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "freeze_symbol", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeTrue();
        freeze.FrozenCalls.Should().ContainSingle()
            .Which.Should().Be(("game_speed", "float", 2.5f));
    }

    [Fact]
    public async Task FreezeAction_With_BoolValue_Should_Call_FreezeBool()
    {
        var profile = BuildProfile(MakeFreezeAction("freeze_symbol", "symbol", "freeze"));
        var (orchestrator, _, freeze, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "fog_enabled",
            ["freeze"] = true,
            ["boolValue"] = false
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "freeze_symbol", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeTrue();
        freeze.FrozenCalls.Should().ContainSingle()
            .Which.Should().Be(("fog_enabled", "bool", false));
    }

    [Fact]
    public async Task UnfreezeAction_Should_Call_Unfreeze_On_Service()
    {
        var profile = BuildProfile(MakeFreezeAction("unfreeze_symbol", "symbol"));
        var (orchestrator, _, freeze, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits"
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "unfreeze_symbol", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Unfroze");
        freeze.UnfrozenCalls.Should().ContainSingle().Which.Should().Be("credits");
    }

    [Fact]
    public async Task FreezeAction_With_Freeze_True_But_No_Value_Should_Fail()
    {
        var profile = BuildProfile(MakeFreezeAction("freeze_symbol", "symbol", "freeze"));
        var (orchestrator, _, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true
            // missing intValue / floatValue / boolValue
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "freeze_symbol", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeFalse();
        var msg = result.Message;
        (msg.Contains("intValue") || msg.Contains("floatValue") || msg.Contains("boolValue"))
            .Should().BeTrue("error message should mention at least one value type field");
    }

    [Fact]
    public async Task FreezeAction_Without_Symbol_Should_Fail_Validation()
    {
        // payloadSchema requires "symbol", so omitting it should be caught by validator
        var profile = BuildProfile(MakeFreezeAction("freeze_symbol", "symbol", "freeze"));
        var (orchestrator, _, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["freeze"] = true,
            ["intValue"] = 100
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "freeze_symbol", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("symbol");
    }

    [Fact]
    public void UnfreezeAll_Should_Delegate_To_FreezeService()
    {
        var profile = BuildProfile();
        var (orchestrator, _, freeze, _) = CreateOrchestrator(profile);

        orchestrator.UnfreezeAll();

        freeze.UnfreezeAllCalled.Should().BeTrue();
    }

    // ─── Freeze vs Memory dispatch routing ──────────────────────────────

    [Fact]
    public async Task FreezeAction_Should_Not_Reach_RuntimeAdapter()
    {
        var profile = BuildProfile(MakeFreezeAction("freeze_symbol", "symbol", "freeze"));
        var (orchestrator, runtime, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true,
            ["intValue"] = 5000
        };

        await orchestrator.ExecuteAsync("test_profile", "freeze_symbol", payload, RuntimeMode.Unknown);

        runtime.ReceivedRequests.Should().BeEmpty("Freeze actions are handled by orchestrator, not runtime adapter");
    }

    [Fact]
    public async Task MemoryAction_Should_Reach_RuntimeAdapter()
    {
        var profile = BuildProfile(MakeMemoryAction("set_credits", RuntimeMode.Unknown, "symbol", "intValue"));
        var (orchestrator, runtime, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 1000
        };

        await orchestrator.ExecuteAsync("test_profile", "set_credits", payload, RuntimeMode.Unknown);

        runtime.ReceivedRequests.Should().ContainSingle()
            .Which.Action.Id.Should().Be("set_credits");
    }

    [Fact]
    public async Task CodePatchAction_Should_Reach_RuntimeAdapter()
    {
        var profile = BuildProfile(MakeCodePatchAction("toggle_instant_build_patch", "enable"));
        var (orchestrator, runtime, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["enable"] = true
        };

        await orchestrator.ExecuteAsync("test_profile", "toggle_instant_build_patch", payload, RuntimeMode.Unknown);

        runtime.ReceivedRequests.Should().ContainSingle()
            .Which.Action.ExecutionKind.Should().Be(ExecutionKind.CodePatch);
    }

    // ─── Mode gating ────────────────────────────────────────────────────

    [Fact]
    public async Task Action_Should_Be_Rejected_When_Mode_Mismatch()
    {
        var profile = BuildProfile(MakeMemoryAction("galactic_only", RuntimeMode.Galactic, "symbol", "intValue"));
        var (orchestrator, _, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 500
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "galactic_only", payload, RuntimeMode.Tactical);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not allowed");
    }

    [Fact]
    public async Task Action_Should_Succeed_When_Mode_Is_Unknown()
    {
        var profile = BuildProfile(MakeMemoryAction("any_mode", RuntimeMode.Galactic, "symbol", "intValue"));
        var (orchestrator, _, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 500
        };

        // When runtime mode is Unknown, mode gate is skipped
        var result = await orchestrator.ExecuteAsync("test_profile", "any_mode", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeTrue();
    }

    // ─── Payload validation ─────────────────────────────────────────────

    [Fact]
    public async Task CodePatch_Missing_Required_Field_Should_Fail()
    {
        var profile = BuildProfile(MakeCodePatchAction("toggle_instant_build_patch", "enable"));
        var (orchestrator, _, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject
        {
            // missing enable
        };

        var result = await orchestrator.ExecuteAsync("test_profile", "toggle_instant_build_patch", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("enable");
    }

    [Fact]
    public async Task Unknown_ActionId_Should_Return_Failure()
    {
        var profile = BuildProfile();
        var (orchestrator, _, _, _) = CreateOrchestrator(profile);

        var payload = new JsonObject { ["symbol"] = "credits" };

        var result = await orchestrator.ExecuteAsync("test_profile", "nonexistent_action", payload, RuntimeMode.Unknown);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("nonexistent_action");
    }
}
