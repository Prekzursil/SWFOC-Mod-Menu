using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterAttachCoverageTests
{
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    [Fact]
    public void Constructor_ShouldProvide_DefaultHelperCommandTransport_For_NonDi_RuntimeAdapter()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with
        {
            HelperModHooks =
            [
                new HelperHookSpec(
                    Id: "spawn_bridge",
                    Script: "scripts/common/spawn_bridge.lua",
                    Version: "1.0.0",
                    EntryPoint: "SWFOC_Trainer_Spawn_Context")
            ]
        };

        var adapter = new RuntimeAdapter(
            new MultiProcessLocator(Array.Empty<ProcessMetadata>()),
            new StubProfileRepository(profile),
            new FixedSignatureResolver(new SymbolMap(new Dictionary<string, SymbolInfo>())),
            NullLogger<RuntimeAdapter>.Instance);

        var helperBridgeField = RuntimeAdapterType.GetField("_helperBridgeBackend", BindingFlags.Instance | BindingFlags.NonPublic);
        helperBridgeField.Should().NotBeNull();
        var helperBridge = helperBridgeField!.GetValue(adapter);
        helperBridge.Should().BeOfType<NamedPipeHelperBridgeBackend>();

        var transportField = typeof(NamedPipeHelperBridgeBackend).GetField("_helperCommandTransportService", BindingFlags.Instance | BindingFlags.NonPublic);
        transportField.Should().NotBeNull();
        transportField!.GetValue(helperBridge).Should().NotBeNull("non-DI runtime flows should still stage helper overlay commands");
    }

    [Fact]
    public async Task PrepareAttachSessionArtifactsAsync_ShouldThrow_WhenProfileHasNoSignatureSets()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with { SignatureSets = Array.Empty<SignatureSet>() };
        var process = BuildProcess(101, "swfoc", @"C:\Games\swfoc.exe");
        var adapter = CreateAdapter(profile, new FixedSignatureResolver(new SymbolMap(new Dictionary<string, SymbolInfo>())));

        var method = RuntimeAdapterType.GetMethod("PrepareAttachSessionArtifactsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var act = async () => await InvokePrivateAsync(method!, adapter, profile, process, profile.Id, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"No signature sets configured for profile '{profile.Id}'");
    }

    [Fact]
    public async Task PrepareAttachSessionArtifactsAsync_ShouldAttachVariantDiagnostics_AndCalibrationArtifact()
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), $"swfoc-attach-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactRoot);

        try
        {
            var profile = ReflectionCoverageVariantFactory.BuildProfile() with
            {
                SignatureSets =
                [
                    new SignatureSet(
                        "default",
                        "steam-64",
                        [new SignatureSpec("credits", "F3 0F 2C 50 70", 0, SignatureAddressMode.HitPlusOffset)])
                ]
            };
            var process = BuildProcess(202, "StarWarsG", @"C:\Games\StarWarsG.exe", mode: RuntimeMode.Unknown);
            var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", (nint)0x1234, SymbolValueType.Int32, AddressSource.Signature)
            });
            var adapter = CreateAdapter(profile, new FixedSignatureResolver(symbols));
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_calibrationArtifactRoot", artifactRoot);

            var method = RuntimeAdapterType.GetMethod("PrepareAttachSessionArtifactsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var variant = new ProfileVariantResolution(profile.Id, "resolved_aotr", "launch_recommendation", 0.73d, "fp-123");
            var result = await InvokePrivateAsync(method!, adapter, profile, process, profile.Id, variant, CancellationToken.None);

            var preparedProcess = GetProperty<ProcessMetadata>(result!, "Process");
            preparedProcess.Metadata.Should().NotBeNull();
            preparedProcess.Metadata.Should().ContainKey("calibrationArtifactPath");
            preparedProcess.Metadata!["resolvedVariant"].Should().Be(profile.Id);
            preparedProcess.Metadata["resolvedVariantReasonCode"].Should().Be("launch_recommendation");
            preparedProcess.Metadata["resolvedVariantConfidence"].Should().Be("0.73");
            preparedProcess.Metadata["resolvedVariantFingerprintId"].Should().Be("fp-123");
            preparedProcess.Metadata["processSelectionReason"].Should().Be("exe_target_match");

            var artifactPath = preparedProcess.Metadata["calibrationArtifactPath"];
            File.Exists(artifactPath).Should().BeTrue();

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(artifactPath));
            doc.RootElement.GetProperty("trigger").GetString().Should().Be("attach");
            doc.RootElement.GetProperty("profile").GetProperty("id").GetString().Should().Be(profile.Id);
            doc.RootElement.GetProperty("process").GetProperty("ProcessId").GetInt32().Should().Be(process.ProcessId);
        }
        finally
        {
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SelectProcessForProfileAsync_ShouldPreferRecommendedWorkshopMatchedHost()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with
        {
            ExeTarget = ExeTarget.Swfoc,
            SteamWorkshopId = "1125571106"
        };

        var weakCandidate = BuildProcess(
            301,
            "swfoc",
            @"C:\Games\swfoc.exe",
            commandLine: "STEAMMOD=1125571106",
            hostRole: ProcessHostRole.Launcher,
            mainModuleSize: 1_000_000);

        var winningCandidate = BuildProcess(
            302,
            "StarWarsG",
            @"C:\Games\StarWarsG.exe",
            commandLine: "STEAMMOD=1125571106",
            hostRole: ProcessHostRole.GameHost,
            mainModuleSize: 2_000_000,
            launchContext: BuildLaunchContext(profile.Id, "workshop_detected", 0.95d));

        var wrongWorkshopCandidate = BuildProcess(
            303,
            "StarWarsG",
            @"C:\Games\StarWarsG.exe",
            commandLine: "STEAMMOD=9999999999",
            hostRole: ProcessHostRole.GameHost,
            mainModuleSize: 3_000_000);

        var adapter = new RuntimeAdapter(
            new MultiProcessLocator([weakCandidate, winningCandidate, wrongWorkshopCandidate]),
            new StubProfileRepository(profile),
            new FixedSignatureResolver(new SymbolMap(new Dictionary<string, SymbolInfo>())),
            NullLogger<RuntimeAdapter>.Instance,
            ReflectionCoverageRuntimeFactory.BuildServiceProvider());

        var method = RuntimeAdapterType.GetMethod("SelectProcessForProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var selected = (ProcessMetadata?)await InvokePrivateAsync(method!, adapter, profile, CancellationToken.None);

        selected.Should().NotBeNull();
        selected!.ProcessId.Should().Be(302);
        selected.HostRole.Should().Be(ProcessHostRole.GameHost);
        selected.WorkshopMatchCount.Should().Be(1);
        selected.SelectionScore.Should().BeGreaterThan(0d);
        selected.Metadata.Should().NotBeNull();
        selected.Metadata!["hostRole"].Should().Be("gamehost");
        selected.Metadata["workshopMatchCount"].Should().Be("1");
        selected.Metadata["selectionScore"].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ApplyHelperBridgeProbeMetadataAsync_ShouldPersistHelperAutoloadDiagnostics()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadStrategy"] = "story_wrapper_chain",
                ["helperAutoloadScripts"] = "Library/PGStoryMode.lua"
            },
            HelperModHooks =
            [
                new HelperHookSpec(
                    Id: "spawn_bridge",
                    Script: "scripts/common/spawn_bridge.lua",
                    Version: "1.0.0",
                    EntryPoint: "SWFOC_Trainer_Spawn_Context")
            ]
        };
        var harness = new AdapterHarness
        {
            HelperBridgeBackend = new StubHelperBridgeBackend
            {
                ProbeResult = new HelperBridgeProbeResult(
                    Available: false,
                    ReasonCode: RuntimeReasonCode.HELPER_VERIFICATION_FAILED,
                    Message: "pending",
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["helperBridgeState"] = "experimental",
                        ["helperAutoloadState"] = "pending_story_mode_load",
                        ["helperAutoloadReasonCode"] = "story_wrapper_waiting_for_story_load",
                        ["helperAutoloadStrategy"] = "story_wrapper_chain",
                        ["helperAutoloadScript"] = "Library/PGStoryMode.lua"
                    })
            }
        };
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);
        var session = RuntimeAdapterExecuteCoverageTests.BuildSession(RuntimeMode.Galactic);

        var method = RuntimeAdapterType.GetMethod("ApplyHelperBridgeProbeMetadataAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var updated = await InvokePrivateAsync(method!, adapter, session, profile, CancellationToken.None);
        var updatedSession = updated.Should().BeOfType<AttachSession>().Subject;

        updatedSession.Process.Metadata.Should().NotBeNull();
        updatedSession.Process.Metadata!["helperBridgeState"].Should().Be("experimental");
        updatedSession.Process.Metadata["helperAutoloadState"].Should().Be("pending_story_mode_load");
        updatedSession.Process.Metadata["helperAutoloadReasonCode"].Should().Be("story_wrapper_waiting_for_story_load");
        updatedSession.Process.Metadata["helperAutoloadStrategy"].Should().Be("story_wrapper_chain");
        updatedSession.Process.Metadata["helperAutoloadScript"].Should().Be("Library/PGStoryMode.lua");
    }

    [Fact]
    public void TryWriteCalibrationScanArtifact_ShouldPersistOutputAndLatestFiles()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile();
        var adapter = CreateAdapter(profile, new FixedSignatureResolver(new SymbolMap(new Dictionary<string, SymbolInfo>())));
        var artifactRoot = Path.Combine(Path.GetTempPath(), $"swfoc-scan-artifacts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactRoot);

        try
        {
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_calibrationArtifactRoot", artifactRoot);

            var candidates = new[]
            {
                new RuntimeCalibrationCandidate(
                    SuggestedPattern: "F3 0F 2C 50 70",
                    Offset: 0,
                    AddressMode: SignatureAddressMode.HitPlusOffset,
                    ValueType: SymbolValueType.Int32,
                    InstructionRva: "0x10",
                    Snippet: "F3 0F 2C",
                    ReferenceCount: 1)
            };

            var method = RuntimeAdapterType.GetMethod("TryWriteCalibrationScanArtifact", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var artifactPath = (string?)method!.Invoke(adapter, ["base/swfoc", "credits value", candidates]);

            artifactPath.Should().NotBeNull();
            File.Exists(artifactPath).Should().BeTrue();
            Path.GetFileName(artifactPath!).Should().StartWith("scan_base_swfoc_credits_value_");

            var latestPath = Path.Combine(artifactRoot, "scans", "scan_latest.json");
            File.Exists(latestPath).Should().BeTrue();

            artifactPath.Should().NotBeNull();
            using var doc = JsonDocument.Parse(File.ReadAllText(artifactPath!));
            doc.RootElement.GetProperty("candidateCount").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("targetSymbol").GetString().Should().Be("credits value");
        }
        finally
        {
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryEmitCalibrationSnapshot_ShouldReturnNull_WhenArtifactRootCannotBeCreated()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with
        {
            SignatureSets = [new SignatureSet("default", "steam-64", [new SignatureSpec("credits", "AA", 0)])]
        };
        var process = BuildProcess(404, "swfoc", @"C:\Games\swfoc.exe");
        var build = new ProfileBuild(profile.Id, "steam-64", process.ProcessPath, process.ExeTarget, process.CommandLine, process.ProcessId);
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new("credits", (nint)0x1234, SymbolValueType.Int32, AddressSource.Signature)
        });

        var blockingFile = Path.Combine(Path.GetTempPath(), $"swfoc-blocking-file-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(blockingFile, "occupied");

        try
        {
            var adapter = CreateAdapter(profile, new FixedSignatureResolver(symbols));
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_calibrationArtifactRoot", blockingFile);

            var method = RuntimeAdapterType.GetMethod("TryEmitCalibrationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = (string?)method!.Invoke(adapter, [profile, process, build, symbols]);

            result.Should().BeNull();
        }
        finally
        {
            if (File.Exists(blockingFile))
            {
                File.Delete(blockingFile);
            }
        }
    }

    [Fact]
    public async Task ScanCalibrationCandidatesAsync_ShouldReportSymbolNotInProfile_WhenSignatureMissing()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with
        {
            SignatureSets = [new SignatureSet("default", "steam-64", [new SignatureSpec("other_symbol", "AA", 0)])]
        };
        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);

        var result = await adapter.ScanCalibrationCandidatesAsync(new RuntimeCalibrationScanRequest("credits"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("symbol_not_in_profile");
        result.Candidates.Should().BeEmpty();
    }

    private static RuntimeAdapter CreateAdapter(TrainerProfile profile, ISignatureResolver signatureResolver)
    {
        return new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(profile),
            signatureResolver,
            NullLogger<RuntimeAdapter>.Instance,
            ReflectionCoverageRuntimeFactory.BuildServiceProvider());
    }

    private static async Task<object?> InvokePrivateAsync(MethodInfo method, object instance, params object?[] args)
    {
        var task = method.Invoke(instance, args).Should().BeAssignableTo<Task>().Subject as Task;
        task.Should().NotBeNull();
        await task!;
        return task!.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull($"property {propertyName} should exist");
        return (T)property!.GetValue(instance)!;
    }

    private static ProcessMetadata BuildProcess(
        int processId,
        string processName,
        string processPath,
        string? commandLine = null,
        RuntimeMode mode = RuntimeMode.Galactic,
        ProcessHostRole hostRole = ProcessHostRole.Unknown,
        int mainModuleSize = 0,
        LaunchContext? launchContext = null)
    {
        return new ProcessMetadata(
            ProcessId: processId,
            ProcessName: processName,
            ProcessPath: processPath,
            CommandLine: commandLine,
            ExeTarget: ExeTarget.Swfoc,
            Mode: mode,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            LaunchContext: launchContext,
            HostRole: hostRole,
            MainModuleSize: mainModuleSize);
    }

    private static LaunchContext BuildLaunchContext(string profileId, string reasonCode, double confidence)
    {
        return new LaunchContext(
            LaunchKind.Workshop,
            CommandLineAvailable: true,
            SteamModIds: ["1125571106"],
            ModPathRaw: null,
            ModPathNormalized: null,
            DetectedVia: "steam",
            Recommendation: new ProfileRecommendation(profileId, reasonCode, confidence),
            Source: "detected");
    }

    private sealed class FixedSignatureResolver(SymbolMap map) : ISignatureResolver
    {
        public Task<SymbolMap> ResolveAsync(
            ProfileBuild build,
            IReadOnlyList<SignatureSet> signatureSets,
            IReadOnlyDictionary<string, long> fallbackOffsets,
            CancellationToken cancellationToken)
        {
            _ = build;
            _ = signatureSets;
            _ = fallbackOffsets;
            _ = cancellationToken;
            return Task.FromResult(map);
        }
    }

    private sealed class MultiProcessLocator(IReadOnlyList<ProcessMetadata> processes) : IProcessLocator
    {
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(processes);
        }

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            _ = cancellationToken;
            return Task.FromResult(processes.FirstOrDefault());
        }
    }
}
