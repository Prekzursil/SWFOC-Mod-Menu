using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// End-to-end integration tests for Task #118 — prove an
/// ActionExecutionRequest survives the full Orchestrator → IRuntimeAdapter
/// → NamedPipeExtenderBackend → named-pipe → Extender.Host pipeline.
///
/// Other test files already cover the layers in isolation:
///   * TrainerOrchestratorTests — Orchestrator against a StubRuntimeAdapter
///   * NamedPipeExtenderBackendTests — the backend with the orchestrator bypassed
///
/// The missing coverage — and the #118 regression — is that a real
/// backend receives the same ActionExecutionRequest the orchestrator
/// produces, without any mock boundary collapsing the shape. Failing
/// the wire format here would have masked an entire class of "works in
/// unit tests, breaks in production" regressions.
/// </summary>
public sealed class OrchestratorPipelineE2ETests
{
    // --- Helpers (mirror the minimal-profile + minimal-session idioms
    //     the existing TrainerOrchestratorTests use). ---

    private static ActionSpec MakeMemoryAction(string id) =>
        new(
            Id: id,
            Category: ActionCategory.Economy,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Memory,
            PayloadSchema: new JsonObject { ["required"] = new JsonArray() },
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "pipeline-e2e action");

    private static TrainerProfile BuildProfile(params ActionSpec[] actions) =>
        new(
            Id: "pipeline_e2e_profile",
            DisplayName: "Pipeline E2E",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions.ToDictionary(a => a.Id),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "",
            HelperModHooks: Array.Empty<HelperHookSpec>());

    private static string CreatePipeName() => $"swfoc_pipeline_e2e_{Guid.NewGuid():N}";

    /// <summary>
    /// A thin IRuntimeAdapter that captures the request it receives then
    /// hands it straight to a real NamedPipeExtenderBackend. This proves
    /// the request shape is preserved from Orchestrator → backend without
    /// standing up the full RuntimeAdapter machinery (5+ service deps
    /// that would drown the test in unrelated setup).
    /// </summary>
    private sealed class ExtenderForwardingRuntimeAdapter : IRuntimeAdapter
    {
        private readonly NamedPipeExtenderBackend _backend;
        private readonly CapabilityReport _capabilityReport;

        public ExtenderForwardingRuntimeAdapter(NamedPipeExtenderBackend backend, string profileId)
        {
            _backend = backend;
            _capabilityReport = CapabilityReport.Unknown(profileId);
        }

        public bool IsAttached => true;
        public AttachSession? CurrentSession { get; set; }
        public List<ActionExecutionRequest> ForwardedRequests { get; } = new();

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged =>
            throw new NotImplementedException();

        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged =>
            throw new NotImplementedException();

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct)
        {
            ForwardedRequests.Add(request);
            return _backend.ExecuteAsync(request, _capabilityReport, ct);
        }

        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;
        public StubProfileRepository(TrainerProfile profile) => _profile = profile;

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken ct = default) =>
            Task.FromResult(_profile);

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken ct = default) =>
            Task.FromResult(_profile);

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
    }

    private sealed class NoopFreezeService : IValueFreezeService
    {
        public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>();
        public void FreezeInt(string symbol, int value) { }
        public void FreezeIntAggressive(string symbol, int value) { }
        public void FreezeFloat(string symbol, float value) { }
        public void FreezeBool(string symbol, bool value) { }
        public bool Unfreeze(string symbol) => false;
        public void UnfreezeAll() { }
        public bool IsFrozen(string symbol) => false;
        public void Dispose() { }
    }

    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<ActionAuditRecord> Records { get; } = new();

        public Task WriteAsync(ActionAuditRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    // --- Tests ---

    [Fact]
    public async Task OrchestratorExecute_PipelineRoundtrip_PreservesFeatureIdAndProfileAndPayload()
    {
        // Arrange: build the full Orchestrator → RuntimeAdapter → Backend chain.
        var pipeName = CreatePipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var backend = new NamedPipeExtenderBackend(pipeName, autoStartBridgeHost: false);
        var profile = BuildProfile(MakeMemoryAction("set_credits"));
        var runtime = new ExtenderForwardingRuntimeAdapter(backend, profile.Id)
        {
            CurrentSession = new AttachSession(
                profile.Id,
                new ProcessMetadata(
                    4242,
                    "StarWarsG",
                    @"C:\Games\swfoc.exe",
                    null,
                    ExeTarget.Swfoc,
                    RuntimeMode.TacticalLand,
                    null,
                    null),
                new ProfileBuild(profile.Id, "test_build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
                new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
                DateTimeOffset.UtcNow),
        };
        var orchestrator = new TrainerOrchestrator(
            new StubProfileRepository(profile),
            runtime,
            new NoopFreezeService(),
            new CapturingAuditLogger());

        // Act: fire the orchestrator call, then play the mock-bridge role on the server.
        var payload = new JsonObject { ["amount"] = 50000 };
        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var resultTask = orchestrator.ExecuteAsync(
            profile.Id,
            "set_credits",
            payload,
            RuntimeMode.TacticalLand);

        await waitTask;
        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };

        // The backend writes exactly one JSON line per request; slurp it.
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var requestDoc = JsonDocument.Parse(requestJson);
        var requestRoot = requestDoc.RootElement;

        // Assert the request shape as it lands on the host:
        requestRoot.GetProperty("featureId").GetString()
            .Should().Be("set_credits", "the ActionSpec.Id must survive through every layer");
        requestRoot.GetProperty("profileId").GetString()
            .Should().Be(profile.Id);
        requestRoot.GetProperty("mode").GetString()
            .Should().Be(nameof(RuntimeMode.TacticalLand));
        requestRoot.GetProperty("payload").GetProperty("amount").GetInt32()
            .Should().Be(50000, "payload fields must not be flattened, renamed, or lost");
        requestRoot.GetProperty("commandId").GetString().Should().NotBeNullOrWhiteSpace();

        // Write a canned OK response back so the orchestrator sees a success result.
        var commandId = requestRoot.GetProperty("commandId").GetString()!;
        var response = JsonSerializer.Serialize(new
        {
            commandId = commandId,
            succeeded = true,
            reasonCode = nameof(RuntimeReasonCode.CAPABILITY_PROBE_PASS),
            backend = "extender",
            hookState = "ok",
            message = "set_credits applied"
        });
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);

        var result = await resultTask;

        // Assert the result shape:
        result.Succeeded.Should().BeTrue("mock bridge returned succeeded=true");
        result.Message.Should().Be("set_credits applied");
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["backend"].Should().Be("extender");
        result.Diagnostics["hookState"].Should().Be("ok");
        result.Diagnostics["extenderCommandId"].Should().Be(commandId);

        // Assert the forwarding adapter actually saw one request — the orchestrator
        // did not short-circuit the runtime layer.
        runtime.ForwardedRequests.Should().HaveCount(1);
        var forwarded = runtime.ForwardedRequests[0];
        forwarded.Action.Id.Should().Be("set_credits");
        forwarded.ProfileId.Should().Be(profile.Id);
        forwarded.RuntimeMode.Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public async Task OrchestratorExecute_WhenBridgeReportsFailure_PropagatesSuccessFalseAndReason()
    {
        // Arrange
        var pipeName = CreatePipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var backend = new NamedPipeExtenderBackend(pipeName, autoStartBridgeHost: false);
        var profile = BuildProfile(MakeMemoryAction("give_credits"));
        var runtime = new ExtenderForwardingRuntimeAdapter(backend, profile.Id)
        {
            CurrentSession = new AttachSession(
                profile.Id,
                new ProcessMetadata(1111, "StarWarsG", @"C:\", null, ExeTarget.Swfoc, RuntimeMode.Galactic, null, null),
                new ProfileBuild(profile.Id, "b", @"C:\", ExeTarget.Swfoc),
                new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
                DateTimeOffset.UtcNow),
        };
        var orchestrator = new TrainerOrchestrator(
            new StubProfileRepository(profile),
            runtime,
            new NoopFreezeService(),
            new CapturingAuditLogger());

        // Act
        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var resultTask = orchestrator.ExecuteAsync(
            profile.Id,
            "give_credits",
            new JsonObject { ["amount"] = 1 },
            RuntimeMode.Galactic);

        await waitTask;
        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var requestDoc = JsonDocument.Parse(requestJson);
        var commandId = requestDoc.RootElement.GetProperty("commandId").GetString()!;

        var response = JsonSerializer.Serialize(new
        {
            commandId = commandId,
            succeeded = false,
            reasonCode = nameof(RuntimeReasonCode.CAPABILITY_ANCHOR_INVALID),
            backend = "extender",
            hookState = "symbol_miss",
            message = "symbol 'credits' not resolved on this profile"
        });
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);

        var result = await resultTask;

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("symbol 'credits' not resolved");
        result.Diagnostics!["reasonCode"].Should().Be(nameof(RuntimeReasonCode.CAPABILITY_ANCHOR_INVALID));
        result.Diagnostics["hookState"].Should().Be("symbol_miss");
    }
}
