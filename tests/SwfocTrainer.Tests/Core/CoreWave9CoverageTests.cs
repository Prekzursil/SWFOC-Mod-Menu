using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CoreWave9CoverageTests
{
    // Exercises IsModeCompatible AnyTactical branch (line 368) via Evaluate
    [Fact]
    public void Evaluate_WhenAnyTactical_AndTacticalLand_ShouldEvaluate()
    {
        var svc = new ActionReliabilityService();
        var profile = BP(actions: new Dictionary<string, ActionSpec>
        {
            ["tac"] = new ActionSpec(Id: "tac", Category: ActionCategory.Unit,
                Mode: RuntimeMode.AnyTactical, ExecutionKind: ExecutionKind.Memory,
                PayloadSchema: new JsonObject(), VerifyReadback: false, CooldownMs: 0)
        });
        var session = BS(RuntimeMode.TacticalLand);
        var results = svc.Evaluate(profile, session, null);
        results.Should().ContainSingle();
        // AnyTactical is compatible with TacticalLand - action should not be mode-blocked
        results[0].State.Should().NotBe(ActionReliabilityState.Unavailable);
    }

    [Fact]
    public void Evaluate_WhenAnyTactical_AndTacticalSpace_ShouldEvaluate()
    {
        var svc = new ActionReliabilityService();
        var profile = BP(actions: new Dictionary<string, ActionSpec>
        {
            ["tac"] = new ActionSpec(Id: "tac", Category: ActionCategory.Unit,
                Mode: RuntimeMode.AnyTactical, ExecutionKind: ExecutionKind.Memory,
                PayloadSchema: new JsonObject(), VerifyReadback: false, CooldownMs: 0)
        });
        var session = BS(RuntimeMode.TacticalSpace);
        var results = svc.Evaluate(profile, session, null);
        results.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_Stable_WhenNoSymbolRequired()
    {
        var svc = new ActionReliabilityService();
        var profile = BP(actions: new Dictionary<string, ActionSpec>
        {
            ["ns"] = new ActionSpec(Id: "ns", Category: ActionCategory.Global,
                Mode: RuntimeMode.Galactic, ExecutionKind: ExecutionKind.Memory,
                PayloadSchema: new JsonObject(), VerifyReadback: false, CooldownMs: 0)
        });
        svc.Evaluate(profile, BS(), null).Should().ContainSingle()
            .Which.State.Should().Be(ActionReliabilityState.Stable);
    }

    [Fact]
    public void Evaluate_NullMetadata_NoThrow()
    {
        var svc = new ActionReliabilityService();
        var profile = BP(metadata: null, actions: new Dictionary<string, ActionSpec>
        {
            ["a"] = new ActionSpec(Id: "a", Category: ActionCategory.Global,
                Mode: RuntimeMode.Galactic, ExecutionKind: ExecutionKind.Memory,
                PayloadSchema: new JsonObject(), VerifyReadback: false, CooldownMs: 0)
        });
        svc.Evaluate(profile, BS(), null).Should().ContainSingle();
    }

    // SelectedUnitTransactionService branches
    [Fact] public async Task Apply_Empty_Fails() { var sut = MakeSUT(); (await sut.ApplyAsync("t", new SelectedUnitDraft(), RuntimeMode.TacticalLand, CancellationToken.None)).Succeeded.Should().BeFalse(); }
    [Fact] public async Task Apply_Unknown_Fails() { var sut = MakeSUT(); (await sut.ApplyAsync("t", new SelectedUnitDraft(Hp: 1f), RuntimeMode.Unknown, CancellationToken.None)).Succeeded.Should().BeFalse(); }
    [Fact] public async Task Apply_Galactic_Fails() { var sut = MakeSUT(); var r = await sut.ApplyAsync("t", new SelectedUnitDraft(Hp: 1f), RuntimeMode.Galactic, CancellationToken.None); r.Succeeded.Should().BeFalse(); r.Message.Should().Contain("tactical"); }
    [Fact] public async Task Revert_NoHistory() { var sut = MakeSUT(); (await sut.RevertLastAsync("t", RuntimeMode.TacticalLand, CancellationToken.None)).Succeeded.Should().BeFalse(); }
    [Fact] public async Task Restore_NoBaseline() { var sut = MakeSUT(); (await sut.RestoreBaselineAsync("t", RuntimeMode.TacticalLand, CancellationToken.None)).Succeeded.Should().BeFalse(); }

    [Fact]
    public async Task Apply_WithActions_ShouldSucceed()
    {
        var sut = MakeSUTWithActions();
        var r = await sut.ApplyAsync("t", new SelectedUnitDraft(Hp: 500f, Veterancy: 3), RuntimeMode.TacticalLand, CancellationToken.None);
        r.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_AllFields_ShouldSucceed()
    {
        var sut = MakeSUTWithActions();
        var r = await sut.ApplyAsync("t", new SelectedUnitDraft(500f, 200f, 10f, 2f, .5f, 3, 1), RuntimeMode.TacticalLand, CancellationToken.None);
        r.Succeeded.Should().BeTrue();
        r.Steps.Should().HaveCount(7);
    }

    [Fact]
    public async Task Revert_AfterApply_ShouldSucceed()
    {
        var sut = MakeSUTWithActions();
        await sut.ApplyAsync("t", new SelectedUnitDraft(Hp: 500f), RuntimeMode.TacticalLand, CancellationToken.None);
        var r = await sut.RevertLastAsync("t", RuntimeMode.TacticalLand, CancellationToken.None);
        r.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_AfterCapture_ShouldSucceed()
    {
        var sut = MakeSUTWithActions();
        await sut.CaptureAsync(CancellationToken.None);
        (await sut.RestoreBaselineAsync("t", RuntimeMode.TacticalLand, CancellationToken.None)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Apply_Fails_WhenExecFails()
    {
        var rt = new FR();
        var orch = new TrainerOrchestrator(new SP(BPFull()), rt, new SF(), new SA());
        var sut = new SelectedUnitTransactionService(rt, orch);
        var r = await sut.ApplyAsync("t", new SelectedUnitDraft(Hp: 999f), RuntimeMode.TacticalLand, CancellationToken.None);
        r.Succeeded.Should().BeFalse();
        r.Message.Should().Contain("Apply failed");
    }

    // Overloads
    [Fact] public async Task Capture_NonCt() { (await MakeSUT().CaptureAsync()).Should().NotBeNull(); }
    [Fact] public async Task Apply_NonCt() { (await MakeSUTWithActions().ApplyAsync("t", new SelectedUnitDraft(Hp: 5f), RuntimeMode.TacticalLand)).Succeeded.Should().BeTrue(); }
    [Fact] public async Task Revert_NonCt() { (await MakeSUT().RevertLastAsync("t", RuntimeMode.TacticalLand)).Succeeded.Should().BeFalse(); }
    [Fact] public async Task Restore_NonCt() { (await MakeSUT().RestoreBaselineAsync("t", RuntimeMode.TacticalLand)).Succeeded.Should().BeFalse(); }

    // Spawn tests
    [Fact] public async Task Spawn_Stop() { var r = await SpS(BPS(), new FR()).ExecuteBatchAsync("t", new SpawnBatchPlan("t", "p", true, new[] { new SpawnBatchItem(1, "u", "E", "A", 0), new SpawnBatchItem(2, "u", "E", "A", 0) }), RuntimeMode.Galactic); r.Succeeded.Should().BeFalse(); r.Message.Should().Contain("stopped"); }
    [Fact] public async Task Spawn_Delay() { (await SpS(BPS()).ExecuteBatchAsync("t", new SpawnBatchPlan("t", "p", false, new[] { new SpawnBatchItem(1, "u", "E", "A", 10) }), RuntimeMode.Galactic)).Succeeded.Should().BeTrue(); }
    [Fact] public async Task Spawn_NoAction() { var r = await SpS(BP()).ExecuteBatchAsync("t", new SpawnBatchPlan("t", "p", false, new[] { new SpawnBatchItem(1, "u", "E", "A", 0) }), RuntimeMode.Galactic); r.Succeeded.Should().BeFalse(); r.Message.Should().Contain("does not expose"); }
    [Fact] public async Task LoadPresets_EmptyUnits() { (await SpS(BP(), catalog: new Dictionary<string, IReadOnlyList<string>> { ["unit_catalog"] = Array.Empty<string>() }).LoadPresetsAsync("test")).Should().BeEmpty(); }
    [Fact] public async Task LoadPresets_NoFaction() { (await SpS(BP(), catalog: new Dictionary<string, IReadOnlyList<string>> { ["unit_catalog"] = new[] { "trooper" } }).LoadPresetsAsync("test"))[0].Faction.Should().Be("EMPIRE"); }

    // TrustedPathPolicy
    [Fact] public void CombineUnderRoot_Blank() { TrustedPathPolicy.CombineUnderRoot(Path.GetTempPath(), "", "child", "  ").Should().Contain("child"); }
    [Fact] public void IsSubPath_Same() { var r = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar); TrustedPathPolicy.IsSubPath(r, r).Should().BeTrue(); }

    // ModCalibrationService
    [Fact]
    public async Task CalibReport_WhitespaceMetadata()
    {
        var sut = new ModCalibrationService(new ActionReliabilityService());
        var s = new AttachSession("t", new ProcessMetadata(1, "g.exe", @"C:\g.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string> { ["dependencyValidation"] = "   " }),
            new ProfileBuild("t", "1.0", @"C:\g.exe", ExeTarget.Swfoc), new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        (await sut.BuildCompatibilityReportAsync(BP(), s)).DependencyStatus.Should().Be(DependencyValidationStatus.Pass);
    }

    // --- Helpers ---
    private static ActionSpec MA(string id) => new(id, ActionCategory.Unit, RuntimeMode.Unknown, ExecutionKind.Memory, new JsonObject(), false, 0);
    private static TrainerProfile BP(Dictionary<string, ActionSpec>? actions = null, Dictionary<string, string>? metadata = null) => new("test", "Test", null, ExeTarget.Swfoc, null, new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) }, new Dictionary<string, long>(), actions ?? new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "schema", Array.Empty<HelperHookSpec>(), metadata);
    private static TrainerProfile BPFull()
    {
        var actions = new Dictionary<string, ActionSpec>
        {
            ["set_selected_hp"] = MA("set_selected_hp"), ["set_selected_shield"] = MA("set_selected_shield"),
            ["set_selected_speed"] = MA("set_selected_speed"), ["set_selected_damage_multiplier"] = MA("set_selected_damage_multiplier"),
            ["set_selected_cooldown_multiplier"] = MA("set_selected_cooldown_multiplier"),
            ["set_selected_veterancy"] = MA("set_selected_veterancy"), ["set_selected_owner_faction"] = MA("set_selected_owner_faction")
        };
        return BP(actions: actions);
    }
    private static TrainerProfile BPS() => BP(actions: new Dictionary<string, ActionSpec> { ["spawn_unit_helper"] = MA("spawn_unit_helper") });
    private static AttachSession BS(RuntimeMode m = RuntimeMode.Galactic) => new("test", new ProcessMetadata(1, "g.exe", @"C:\g.exe", null, ExeTarget.Swfoc, m, null), new ProfileBuild("test", "1.0", @"C:\g.exe", ExeTarget.Swfoc), new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
    private static SelectedUnitTransactionService MakeSUT() { var rt = new SR1(BS(RuntimeMode.TacticalLand)); return new SelectedUnitTransactionService(rt, new TrainerOrchestrator(new SP(BP()), rt, new SF(), new SA())); }
    private static SelectedUnitTransactionService MakeSUTWithActions() { var rt = new SR1(BS(RuntimeMode.TacticalLand)); return new SelectedUnitTransactionService(rt, new TrainerOrchestrator(new SP(BPFull()), rt, new SF(), new SA())); }
    private static SpawnPresetService SpS(TrainerProfile p, IRuntimeAdapter? rt = null, string? presetRoot = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null) { var ort = rt ?? new SR1(BS()); var o = new TrainerOrchestrator(new SP(p), ort, new SF(), new SA()); return new SpawnPresetService(new SP(p), new SC(catalog), o, new LiveOpsOptions { PresetRootPath = presetRoot ?? Path.Join(Path.GetTempPath(), $"p_{Guid.NewGuid():N}") }); }
    private sealed class SP : IProfileRepository { private readonly TrainerProfile _p; public SP(TrainerProfile p) { _p = p; } public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>())); public Task<ProfileManifest> LoadManifestAsync() => LoadManifestAsync(CancellationToken.None); public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(new[] { _p.Id }); public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(_p); public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(_p); public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask; }
    private sealed class SR1 : IRuntimeAdapter { private readonly AttachSession? _s; public SR1(AttachSession? s = null) { _s = s; } public bool IsAttached => _s is not null; public AttachSession? CurrentSession => _s; public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotSupportedException(); public Task<T> ReadAsync<T>(string sym, CancellationToken ct) where T : unmanaged => Task.FromResult(default(T)); public Task WriteAsync<T>(string sym, T v, CancellationToken ct) where T : unmanaged => Task.CompletedTask; public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest r, CancellationToken ct) => Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature)); public Task DetachAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class FR : IRuntimeAdapter { public bool IsAttached => true; public AttachSession? CurrentSession => BS(RuntimeMode.TacticalLand); public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotSupportedException(); public Task<T> ReadAsync<T>(string sym, CancellationToken ct) where T : unmanaged => Task.FromResult(default(T)); public Task WriteAsync<T>(string sym, T v, CancellationToken ct) where T : unmanaged => Task.CompletedTask; public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest r, CancellationToken ct) => Task.FromResult(new ActionExecutionResult(false, "fail", AddressSource.None)); public Task DetachAsync(CancellationToken ct) => Task.CompletedTask; }
    private sealed class SF : IValueFreezeService { public void FreezeInt(string s, int v) { } public void FreezeIntAggressive(string s, int v) { } public void FreezeFloat(string s, float v) { } public void FreezeBool(string s, bool v) { } public bool Unfreeze(string s) => true; public void UnfreezeAll() { } public bool IsFrozen(string s) => false; public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>(); public void Dispose() { } }
    private sealed class SA : IAuditLogger { public Task WriteAsync(ActionAuditRecord r, CancellationToken ct) => Task.CompletedTask; }
    private sealed class SC : ICatalogService { private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>? _c; public SC(IReadOnlyDictionary<string, IReadOnlyList<string>>? c = null) { _c = c; } public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id, CancellationToken ct) => Task.FromResult(_c ?? (IReadOnlyDictionary<string, IReadOnlyList<string>>)new Dictionary<string, IReadOnlyList<string>>()); public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id) => LoadCatalogAsync(id, CancellationToken.None); }
}