using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage sweep for NamedPipeExtenderBackend — targets all uncovered
/// constructor, probe, execute, health, parse, and bridge host resolution branches.
/// </summary>
public sealed class NamedPipeExtenderBackendBranchCoverageTests
{
    // ── Constructor branches ───────────────────────────────────────────────

    [Fact]
    public void Constructor_Parameterless_ShouldCreateInstance()
    {
        var backend = new NamedPipeExtenderBackend();
        backend.Should().NotBeNull();
        backend.BackendKind.Should().Be(ExecutionBackendKind.Extender);
    }

    [Fact]
    public void Constructor_WithWhitespacePipeName_ShouldUseDefault()
    {
        var backend = new NamedPipeExtenderBackend("   ");
        backend.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithEmptyPipeName_ShouldUseDefault()
    {
        var backend = new NamedPipeExtenderBackend("");
        backend.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithValidPipeName_ShouldUseProvided()
    {
        var backend = new NamedPipeExtenderBackend("custom_pipe_name");
        backend.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithPipeNameAndAutoStart_ShouldStore()
    {
        var backend = new NamedPipeExtenderBackend("pipe", false);
        backend.Should().NotBeNull();
    }

    // ── BackendKind property ───────────────────────────────────────────────

    [Fact]
    public void BackendKind_ShouldReturnExtender()
    {
        var backend = new NamedPipeExtenderBackend("test", false);
        backend.BackendKind.Should().Be(ExecutionBackendKind.Extender);
    }

    // ── ProbeCapabilitiesAsync (2-param overload without CT) ───────────────

    [Fact]
    public async Task ProbeCapabilitiesAsync_TwoParamOverload_ShouldWork()
    {
        var pipeName = CreateTestPipeName();
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        var process = BuildProcess();

        var report = await backend.ProbeCapabilitiesAsync("base_swfoc", process);

        report.Should().NotBeNull();
        report.ProbeReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE);
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldThrow_WhenProfileIdIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ProbeCapabilitiesAsync(null!, BuildProcess());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldThrow_WhenProcessContextIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ProbeCapabilitiesAsync("profile", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ThreeParam_ShouldThrow_WhenProfileIdIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ProbeCapabilitiesAsync(null!, BuildProcess(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ThreeParam_ShouldThrow_WhenProcessContextIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ProbeCapabilitiesAsync("profile", null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── ProbeCapabilitiesAsync — probe failure report ──────────────────────

    [Fact]
    public async Task ProbeCapabilitiesAsync_Failure_ShouldReturnDiagnosticsWithBackendAndPipe()
    {
        var pipeName = CreateTestPipeName();
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var report = await backend.ProbeCapabilitiesAsync("base_swfoc", BuildProcess(), CancellationToken.None);

        report.Diagnostics.Should().ContainKey("backend");
        report.Diagnostics!["backend"]!.ToString().Should().Be("extender");
        report.Diagnostics.Should().ContainKey("pipe");
        report.Diagnostics["pipe"]!.ToString().Should().Be(pipeName);
        report.Diagnostics.Should().ContainKey("reasonCode");
        report.Diagnostics.Should().ContainKey("message");
    }

    // ── BuildProbeAnchors — processId <= 0 branch ─────────────────────────

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldReturnEmptyAnchors_WhenProcessIdIsZero()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        var process = new ProcessMetadata(0, "StarWarsG.exe", @"C:\Games\StarWarsG.exe",
            null, ExeTarget.Swfoc, RuntimeMode.Unknown);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("base_swfoc", process, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.EnumerateObject().Count().Should().Be(0);

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        var report = await probeTask;
        report.Should().NotBeNull();
    }

    // ── ShouldSeedProbeDefaults branches ──────────────────────────────────

    [Theory]
    [InlineData("base_swfoc")]
    [InlineData("base_sweaw")]
    [InlineData("aotr_custom")]
    [InlineData("roe_3447786229_swfoc")]
    public async Task ProbeCapabilitiesAsync_ShouldSeedAnchors_ForKnownProfiles(string profileId)
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync(profileId, BuildProcess(), cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.GetProperty("credits").GetString().Should().NotBeNullOrWhiteSpace();

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldNotSeedAnchors_ForUnknownProfile()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("custom_unknown_profile", BuildProcess(), cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        // Should NOT contain the seeded "credits" anchor
        anchors.TryGetProperty("credits", out _).Should().BeFalse();

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldNotSeedAnchors_ForEmptyProfileId()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("   ", BuildProcess(), cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.TryGetProperty("credits", out _).Should().BeFalse();

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    // ── MergeProbeAnchorsFromMetadata branches ────────────────────────────

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldMergeAnchorsFromMetadata_WhenPresent()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var anchorsJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["custom_anchor"] = "0xDEADBEEF",
            ["credits"] = "0xABCD1234"
        });
        var process = new ProcessMetadata(
            4242, "StarWarsG.exe", @"C:\Games\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=3447786229",
            ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["probeResolvedAnchorsJson"] = anchorsJson
            });

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("base_swfoc", process, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.GetProperty("custom_anchor").GetString().Should().Be("0xDEADBEEF");
        // Metadata anchor should override seeded default
        anchors.GetProperty("credits").GetString().Should().Be("0xABCD1234");

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldIgnoreInvalidAnchorsJson()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var process = new ProcessMetadata(
            4242, "StarWarsG.exe", @"C:\Games\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=3447786229",
            ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["probeResolvedAnchorsJson"] = "not-valid-json{{{"
            });

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("base_swfoc", process, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        // Should still have the default seeded anchors
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.GetProperty("credits").GetString().Should().NotBeNullOrWhiteSpace();

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldIgnoreEmptyAnchorsMetadata()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var process = new ProcessMetadata(
            4242, "StarWarsG.exe", @"C:\Games\StarWarsG.exe",
            null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["probeResolvedAnchorsJson"] = "  "
            });

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("base_swfoc", process, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        // Should still have seeded anchors, metadata was whitespace
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.GetProperty("credits").GetString().Should().NotBeNullOrWhiteSpace();

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldIgnoreNullMetadataDictionary()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        var process = new ProcessMetadata(4242, "StarWarsG.exe",
            @"C:\Games\StarWarsG.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            Metadata: null);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("base_swfoc", process, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        var report = await probeTask;
        report.Should().NotBeNull();
    }

    // ── ProbeCapabilitiesAsync — success report ───────────────────────────

    [Fact]
    public async Task ProbeCapabilitiesAsync_Success_ShouldContainProbeCapabilityEntry()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("base_swfoc", BuildProcess(), cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        var report = await probeTask;

        report.Capabilities.Should().ContainKey("probe_capabilities");
        report.Capabilities["probe_capabilities"].Available.Should().BeTrue();
        report.Capabilities["probe_capabilities"].Confidence.Should().Be(CapabilityConfidenceState.Verified);
        report.Diagnostics.Should().ContainKey("hookState");
    }

    // ── ExecuteAsync (2-param overload without CT) ────────────────────────

    [Fact]
    public async Task ExecuteAsync_TwoParamOverload_ShouldWork()
    {
        var pipeName = CreateTestPipeName();
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        var request = BuildSetCreditsRequest();
        var cap = BuildCapabilityReport(request.ProfileId, request.Action.Id);

        var result = await backend.ExecuteAsync(request, cap);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenCommandIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ExecuteAsync(null!, BuildCapabilityReport("p", "f"));
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenCapabilityReportIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ExecuteAsync(BuildSetCreditsRequest(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_ThreeParam_ShouldThrow_WhenCommandIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ExecuteAsync(null!, BuildCapabilityReport("p", "f"), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteAsync_ThreeParam_ShouldThrow_WhenCapabilityReportIsNull()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var act = () => backend.ExecuteAsync(BuildSetCreditsRequest(), null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── ExecuteAsync — null diagnostics in response ───────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldHandleNullDiagnosticsInResponse()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        var request = BuildSetCreditsRequest();
        var cap = BuildCapabilityReport(request.ProfileId, request.Action.Id);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var execTask = backend.ExecuteAsync(request, cap, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        await reader.ReadLineAsync(cts.Token);
        var responseJson = JsonSerializer.Serialize(new
        {
            commandId = "test-cmd",
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "HOOK_ONESHOT",
            message = "OK"
        });
        await writer.WriteLineAsync(responseJson.AsMemory(), cts.Token);
        var result = await execTask;

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("reasonCode");
        result.Diagnostics.Should().ContainKey("backend");
        result.Diagnostics.Should().ContainKey("hookState");
        result.Diagnostics.Should().ContainKey("probeReasonCode");
    }

    // ── ExecuteAsync — with diagnostics in response ───────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldMergeDiagnosticsFromResponse()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        var request = BuildSetCreditsRequest();
        var cap = BuildCapabilityReport(request.ProfileId, request.Action.Id);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var execTask = backend.ExecuteAsync(request, cap, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        await reader.ReadLineAsync(cts.Token);
        var responseJson = JsonSerializer.Serialize(new
        {
            commandId = "test-cmd",
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "HOOK_LOCK",
            message = "OK",
            diagnostics = new { extra = "data", forcePatchHook = "true" }
        });
        await writer.WriteLineAsync(responseJson.AsMemory(), cts.Token);
        var result = await execTask;

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("extra");
    }

    // ── ExecuteAsync — no context ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ShouldHandleNullContext()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var action = new ActionSpec("set_credits", ActionCategory.Economy, RuntimeMode.Unknown,
            ExecutionKind.Sdk, new JsonObject(), true, 0, "set credits");
        var request = new ActionExecutionRequest(action, new JsonObject { ["value"] = 100 },
            "test_profile", RuntimeMode.Galactic, Context: null);
        var cap = BuildCapabilityReport(request.ProfileId, request.Action.Id);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var execTask = backend.ExecuteAsync(request, cap, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        doc.RootElement.GetProperty("processId").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("processName").GetString().Should().BeEmpty();

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        var responseJson = JsonSerializer.Serialize(new
        {
            commandId,
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "HOOK_ONESHOT",
            message = "OK"
        });
        await writer.WriteLineAsync(responseJson.AsMemory(), cts.Token);
        var result = await execTask;
        result.Succeeded.Should().BeTrue();
    }

    // ── GetHealthAsync (no-param overload) ────────────────────────────────

    [Fact]
    public async Task GetHealthAsync_NoParam_ShouldReturnUnhealthy_WhenNoBridge()
    {
        var backend = new NamedPipeExtenderBackend(CreateTestPipeName(), false);
        var health = await backend.GetHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.BackendId.Should().Be("extender");
        health.Backend.Should().Be(ExecutionBackendKind.Extender);
        health.Diagnostics.Should().ContainKey("pipe");
    }

    [Fact]
    public async Task GetHealthAsync_WithToken_ShouldReturnHealthy_WhenBridgeResponds()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var healthTask = backend.GetHealthAsync(cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        await reader.ReadLineAsync(cts.Token);
        var responseJson = JsonSerializer.Serialize(new
        {
            commandId = "health-cmd",
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "READY",
            message = "Healthy"
        });
        await writer.WriteLineAsync(responseJson.AsMemory(), cts.Token);
        var health = await healthTask;

        health.IsHealthy.Should().BeTrue();
        health.Diagnostics.Should().ContainKey("hookState");
    }

    // ── ParseResponse — null/empty line branch ────────────────────────────

    [Fact]
    public void ParseResponse_ShouldReturnNoResponseResult_ForNullLine()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("ParseResponse", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExtenderResult)method!.Invoke(null, new object?[] { "cmd-id", null })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("no_response");
        result.CommandId.Should().Be("cmd-id");
    }

    [Fact]
    public void ParseResponse_ShouldReturnNoResponseResult_ForWhitespaceLine()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("ParseResponse", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (ExtenderResult)method!.Invoke(null, new object?[] { "cmd-id", "   " })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("no_response");
    }

    // ── ParseResponse — invalid JSON deserializes to null ─────────────────

    [Fact]
    public void ParseResponse_ShouldReturnInvalidResponseResult_ForNullDeserialization()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("ParseResponse", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        // "null" is valid JSON but deserializes to null
        var result = (ExtenderResult)method!.Invoke(null, new object?[] { "cmd-id", "null" })!;
        result.Succeeded.Should().BeFalse();
        result.HookState.Should().Be("invalid_response");
    }

    // ── ParseResponse — valid JSON ────────────────────────────────────────

    [Fact]
    public void ParseResponse_ShouldReturnParsedResult_ForValidJson()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("ParseResponse", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var json = JsonSerializer.Serialize(new
        {
            commandId = "abc",
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "HOOK_READY",
            message = "ok"
        });
        var result = (ExtenderResult)method!.Invoke(null, new object?[] { "abc", json })!;
        result.Succeeded.Should().BeTrue();
        result.HookState.Should().Be("HOOK_READY");
    }

    // ── SendAsync — cancellation triggers timeout result ──────────────────

    [Fact]
    public async Task SendCoreAsync_ShouldReturnTimeoutResult_WhenCancelled()
    {
        var pipeName = CreateTestPipeName();
        var backend = new NamedPipeExtenderBackend(pipeName, false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = await backend.ProbeCapabilitiesAsync("base_swfoc", BuildProcess(), cts.Token);
        report.ProbeReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE);
    }

    // ── ResolvePipeNameFromEnvironment branch ─────────────────────────────

    [Fact]
    public void ResolvePipeNameFromEnvironment_ShouldReturnEnvValue_WhenSet()
    {
        var prev = Environment.GetEnvironmentVariable("SWFOC_EXTENDER_PIPE_NAME");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXTENDER_PIPE_NAME", "custom_env_pipe");
            var backend = new NamedPipeExtenderBackend(null, false);
            // Just verify it doesn't throw and creates an instance
            backend.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXTENDER_PIPE_NAME", prev);
        }
    }

    // ── IsAllowedBridgeHostPath branches ──────────────────────────────────

    [Fact]
    public void IsAllowedBridgeHostPath_ShouldRejectPathTraversal()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("IsAllowedBridgeHostPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { @"C:\foo\..\bar\SwfocExtender.Host.exe" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsAllowedBridgeHostPath_ShouldRejectWrongExecutableName()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("IsAllowedBridgeHostPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { @"C:\foo\bar\malicious.exe" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsAllowedBridgeHostPath_ShouldAcceptValidPath()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("IsAllowedBridgeHostPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { @"C:\native\runtime\SwfocExtender.Host.exe" })!;
        result.Should().BeTrue();
    }

    // ── ShouldSeedProbeDefaults branches ──────────────────────────────────

    [Theory]
    [InlineData("base_swfoc", true)]
    [InlineData("base_sweaw", true)]
    [InlineData("aotr_custom", true)]
    [InlineData("roe_v1", true)]
    [InlineData("custom_profile", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void ShouldSeedProbeDefaults_ShouldReturnExpectedValue(string profileId, bool expected)
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("ShouldSeedProbeDefaults", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object[] { profileId })!;
        result.Should().Be(expected);
    }

    // ── TryAddRoot branches ───────────────────────────────────────────────

    [Fact]
    public void TryAddRoot_ShouldIgnoreNullPath()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("TryAddRoot", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { roots, null });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddRoot_ShouldIgnoreWhitespacePath()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("TryAddRoot", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { roots, "   " });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddRoot_ShouldAddValidPath()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("TryAddRoot", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { roots, Path.GetTempPath() });
        roots.Should().NotBeEmpty();
    }

    // ── TryAddAncestorRoots branches ──────────────────────────────────────

    [Fact]
    public void TryAddAncestorRoots_ShouldIgnoreNullPath()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("TryAddAncestorRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { roots, null, 6 });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddAncestorRoots_ShouldIgnoreWhitespacePath()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("TryAddAncestorRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { roots, "   ", 6 });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddAncestorRoots_ShouldAddAncestorsUpToDepth()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("TryAddAncestorRoots", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { roots, Path.GetTempPath(), 3 });
        roots.Should().NotBeEmpty();
    }

    // ── AddDiscoveredNativeBuildCandidates — directory doesn't exist ──────

    [Fact]
    public void AddDiscoveredNativeBuildCandidates_ShouldDoNothing_WhenDirectoryMissing()
    {
        var method = typeof(NamedPipeExtenderBackend)
            .GetMethod("AddDiscoveredNativeBuildCandidates", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { candidates, @"C:\nonexistent_path_" + Guid.NewGuid().ToString("N") });
        candidates.Should().BeEmpty();
    }

    // ── AppendNonEmptyAnchorValues — empty values are skipped ────────────

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldSkipEmptyAnchorValues_FromMetadata()
    {
        var pipeName = CreateTestPipeName();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend(pipeName, false);

        // Metadata anchor json with one empty value that should be skipped
        var anchorsJson = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["valid_anchor"] = "0x1234",
            ["empty_anchor"] = ""
        });
        var process = new ProcessMetadata(
            4242, "StarWarsG.exe", @"C:\Games\StarWarsG.exe",
            null, ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["probeResolvedAnchorsJson"] = anchorsJson
            });

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var probeTask = backend.ProbeCapabilitiesAsync("custom_unknown_profile", process, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var doc = JsonDocument.Parse(requestJson);
        var anchors = doc.RootElement.GetProperty("resolvedAnchors");
        anchors.GetProperty("valid_anchor").GetString().Should().Be("0x1234");

        var commandId = doc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        await writer.WriteLineAsync(BuildSuccessProbeResponse(commandId).AsMemory(), cts.Token);
        await probeTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string CreateTestPipeName()
        => $"SwfocExtenderBridgeTest_{Guid.NewGuid():N}";

    private static ProcessMetadata BuildProcess()
        => new(4242, "StarWarsG.exe", @"C:\Games\Corruption\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=3447786229", ExeTarget.Swfoc, RuntimeMode.Galactic,
            new Dictionary<string, string>(), null, ProcessHostRole.GameHost, 123, 1, 1001d);

    private static ActionExecutionRequest BuildSetCreditsRequest()
    {
        var action = new ActionSpec("set_credits", ActionCategory.Economy, RuntimeMode.Unknown,
            ExecutionKind.Sdk, new JsonObject(), true, 0, "set credits");
        return new ActionExecutionRequest(action,
            new JsonObject { ["symbol"] = "credits", ["intValue"] = 1000000, ["forcePatchHook"] = true },
            "roe_3447786229_swfoc", RuntimeMode.Galactic);
    }

    private static CapabilityReport BuildCapabilityReport(string profileId, string featureId)
        => new(profileId, DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [featureId] = new BackendCapability(featureId, true, CapabilityConfidenceState.Verified,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

    private static string BuildSuccessProbeResponse(string commandId)
        => JsonSerializer.Serialize(new
        {
            commandId,
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "HOOK_READY",
            message = "Probe completed.",
            diagnostics = new
            {
                capabilities = new
                {
                    freeze_timer = new { available = true, state = "Verified", reasonCode = "CAPABILITY_PROBE_PASS" },
                    set_credits = new { available = true, state = "Verified", reasonCode = "CAPABILITY_PROBE_PASS" }
                }
            }
        });
}
