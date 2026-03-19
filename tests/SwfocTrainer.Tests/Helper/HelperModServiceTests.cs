using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Helper.Config;
using SwfocTrainer.Helper.Services;
using Xunit;
using System.Text.Json;

namespace SwfocTrainer.Tests.Helper;

public sealed class HelperModServiceTests
{
    [Fact]
    public async Task DeployAsync_ShouldCopyDeclaredScripts()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var scriptPath = WriteScript(sourceRoot, "common/spawn_bridge.lua", "-- script body");
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);

            deployedRoot.Should().Be(Path.Combine(installRoot, "base_swfoc"));
            var copiedScript = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            File.Exists(copiedScript).Should().BeTrue();
            File.ReadAllText(copiedScript).Should().Be(File.ReadAllText(scriptPath));
            File.Exists(Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua")).Should().BeTrue();
            File.Exists(Path.Combine(deployedRoot, "helper-deployment.json")).Should().BeTrue();
            Directory.Exists(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "pending")).Should().BeTrue();
            Directory.Exists(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "claimed")).Should().BeTrue();
            Directory.Exists(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "receipts")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldGenerateBootstrapLoader_WithRequireEntriesForEachHook()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- spawn");
            WriteScript(sourceRoot, "scripts/roe/respawn_bridge.lua", "-- respawn");
            var hooks = new[]
            {
                new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn"),
                new HelperHookSpec("roe_respawn_bridge", "scripts/roe/respawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Toggle_Respawn")
            };

            var service = BuildService(BuildProfile("base_swfoc", hooks), sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            var bootstrapPath = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua");

            var bootstrap = File.ReadAllText(bootstrapPath);
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_PROFILE = \"base_swfoc\"");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_HOOK_COUNT = 2");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_HOOKS = {");
            bootstrap.Should().Contain("requirePath = \"common.spawn_bridge\"");
            bootstrap.Should().Contain("requirePath = \"roe.respawn_bridge\"");
            bootstrap.Should().Contain("entryPoint = \"SWFOC_Trainer_Spawn\"");
            bootstrap.Should().Contain("entryPoint = \"SWFOC_Trainer_Toggle_Respawn\"");
            bootstrap.Should().Contain("function SwfocTrainer_Helper_Bootstrap_LoadAll()");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT = \"overlay_command_inbox\"");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_COMMAND_PENDING = \"SwfocTrainer/Runtime/commands/pending\"");
            bootstrap.Should().Contain("SWFOC_TRAINER_HELPER_COMMAND_DISPATCH = ");
            bootstrap.Should().Contain("function SwfocTrainer_Helper_Bootstrap_DescribeTransport()");
            bootstrap.Should().Contain("function SwfocTrainer_Helper_Bootstrap_Execute_Command(command)");
            bootstrap.Should().Contain("function SwfocTrainer_Helper_Bootstrap_Pump()");
            bootstrap.Should().Contain("pcall(dofile, SWFOC_TRAINER_HELPER_COMMAND_DISPATCH)");
            bootstrap.Should().Contain("SwfocTrainer_Helper_Bootstrap_Write_Receipt(");
            bootstrap.Should().Contain("local fn = _G[entryPoint]");
            bootstrap.Should().Contain("pcall(require, hook.requirePath)");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldWriteManifest_WithHookMetadataAndHashes()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var scriptBody = "-- deployed helper";
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", scriptBody);
            var hooks = new[]
            {
                new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.2.3", EntryPoint: "SWFOC_Trainer_Spawn")
            };

            var service = BuildService(BuildProfile("base_swfoc", hooks), sourceRoot, installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            var manifestPath = Path.Combine(deployedRoot, "helper-deployment.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

            var root = document.RootElement;
            root.GetProperty("profileId").GetString().Should().Be("base_swfoc");
            root.GetProperty("bootstrapScript").GetString().Should().Be("Data/Scripts/Library/SwfocTrainer_HelperBootstrap.lua");
            var commandTransport = root.GetProperty("commandTransport");
            commandTransport.GetProperty("model").GetString().Should().Be("overlay_command_inbox");
            commandTransport.GetProperty("schemaVersion").GetString().Should().Be("1.0");
            commandTransport.GetProperty("pendingDirectory").GetString().Should().Be("SwfocTrainer/Runtime/commands/pending");
            commandTransport.GetProperty("claimedDirectory").GetString().Should().Be("SwfocTrainer/Runtime/commands/claimed");
            commandTransport.GetProperty("receiptDirectory").GetString().Should().Be("SwfocTrainer/Runtime/receipts");
            commandTransport.GetProperty("dispatchCommandPath").GetString().Should().Be("SwfocTrainer/Runtime/commands/dispatch.lua");
            commandTransport.GetProperty("dispatchFileFormat").GetString().Should().Be("lua_table");
            commandTransport.GetProperty("executionMode").GetString().Should().Be("bootstrap_dispatch_ready");
            var hooksElement = root.GetProperty("hooks");
            hooksElement.GetArrayLength().Should().Be(1);
            var hook = hooksElement[0];
            hook.GetProperty("id").GetString().Should().Be("spawn_bridge");
            hook.GetProperty("script").GetString().Should().Be("scripts/common/spawn_bridge.lua");
            hook.GetProperty("deployedScript").GetString().Should().Be("Data/Scripts/Library/common/spawn_bridge.lua");
            hook.GetProperty("requirePath").GetString().Should().Be("common.spawn_bridge");
            hook.GetProperty("entryPoint").GetString().Should().Be("SWFOC_Trainer_Spawn");
            hook.GetProperty("version").GetString().Should().Be("1.2.3");
            hook.GetProperty("sha256").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task GetLayoutAsync_ShouldReturnDeterministicTransportPaths()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")]),
                sourceRoot,
                installRoot);

            var layout = await ((IHelperCommandTransportService)service).GetLayoutAsync("base_swfoc", CancellationToken.None);

            layout.ProfileId.Should().Be("base_swfoc");
            layout.Model.Should().Be("overlay_command_inbox");
            layout.SchemaVersion.Should().Be("1.0");
            layout.DispatchCommandPath.Should().EndWith(Path.Combine("SwfocTrainer", "Runtime", "commands", "dispatch.lua"));
            layout.PendingDirectory.Should().EndWith(Path.Combine("SwfocTrainer", "Runtime", "commands", "pending"));
            layout.ClaimedDirectory.Should().EndWith(Path.Combine("SwfocTrainer", "Runtime", "commands", "claimed"));
            layout.ReceiptDirectory.Should().EndWith(Path.Combine("SwfocTrainer", "Runtime", "receipts"));
            File.Exists(layout.ManifestPath).Should().BeTrue();
            File.Exists(layout.BootstrapScriptPath).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task StageCommandAsync_ShouldWritePendingCommandEnvelope_AndResetStaleArtifacts()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")]),
                sourceRoot,
                installRoot);
            var transport = (IHelperCommandTransportService)service;

            var stagedInitial = await transport.StageCommandAsync(
                "base_swfoc",
                "spawn_context_entity",
                "SWFOC_Trainer_Spawn_Context",
                "token-1234",
                new System.Text.Json.Nodes.JsonObject
                {
                    ["entityId"] = "EMP_ATAT",
                    ["faction"] = "Empire"
                },
                CancellationToken.None);

            File.WriteAllText(stagedInitial.ClaimPath, "{}");
            File.WriteAllText(stagedInitial.ReceiptPath, "{}");
            File.WriteAllText(stagedInitial.PayloadPath, "return { stale = true }");

            var staged = await transport.StageCommandAsync(
                "base_swfoc",
                "spawn_context_entity",
                "SWFOC_Trainer_Spawn_Context",
                "token-1234",
                new System.Text.Json.Nodes.JsonObject
                {
                    ["entityId"] = "EMP_ATAT",
                    ["faction"] = "Empire"
                },
                CancellationToken.None);

            staged.CommandPath.Should().EndWith("token-1234.json");
            File.Exists(staged.CommandPath).Should().BeTrue();
            File.Exists(staged.ClaimPath).Should().BeFalse();
            File.Exists(staged.ReceiptPath).Should().BeFalse();
            staged.PayloadPath.Should().EndWith(Path.Combine("SwfocTrainer", "Runtime", "commands", "dispatch.lua"));
            File.Exists(staged.PayloadPath).Should().BeTrue();

            using var document = JsonDocument.Parse(File.ReadAllText(staged.CommandPath));
            var root = document.RootElement;
            root.GetProperty("transportModel").GetString().Should().Be("overlay_command_inbox");
            root.GetProperty("profileId").GetString().Should().Be("base_swfoc");
            root.GetProperty("actionId").GetString().Should().Be("spawn_context_entity");
            root.GetProperty("helperEntryPoint").GetString().Should().Be("SWFOC_Trainer_Spawn_Context");
            root.GetProperty("operationToken").GetString().Should().Be("token-1234");
            root.GetProperty("payload").GetProperty("entityId").GetString().Should().Be("EMP_ATAT");
            root.GetProperty("payload").GetProperty("helperEntryPoint").GetString().Should().Be("SWFOC_Trainer_Spawn_Context");
            root.GetProperty("payload").GetProperty("operationToken").GetString().Should().Be("token-1234");

            var dispatch = File.ReadAllText(staged.PayloadPath);
            dispatch.Should().Contain("return {");
            dispatch.Should().Contain("[\"actionId\"] = \"spawn_context_entity\"");
            dispatch.Should().Contain("[\"helperEntryPoint\"] = \"SWFOC_Trainer_Spawn_Context\"");
            dispatch.Should().Contain("[\"operationToken\"] = \"token-1234\"");
            dispatch.Should().Contain("[\"payload\"] = {");
            dispatch.Should().Contain("[\"entityId\"] = \"EMP_ATAT\"");
            dispatch.Should().Contain("[\"faction\"] = \"Empire\"");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task TryReadReceiptAsync_ShouldParseAppliedReceipt()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")]),
                sourceRoot,
                installRoot);
            var transport = (IHelperCommandTransportService)service;

            var layout = await transport.GetLayoutAsync("base_swfoc", CancellationToken.None);
            Directory.CreateDirectory(layout.ReceiptDirectory);
            var receiptPath = Path.Combine(layout.ReceiptDirectory, "token-verified.json");
            await File.WriteAllTextAsync(
                receiptPath,
                """
                {
                  "operationToken": "token-verified",
                  "actionId": "spawn_context_entity",
                  "helperEntryPoint": "SWFOC_Trainer_Spawn_Context",
                  "status": "applied",
                  "helperVerifyState": "receipt_present",
                  "reasonCode": "overlay_receipt_applied",
                  "message": "Overlay applied command.",
                  "verificationSource": "C:/Games/_LogFile.txt",
                  "appliedEntityId": "EMP_ATAT"
                }
                """);

            var receipt = await transport.TryReadReceiptAsync("base_swfoc", "token-verified", CancellationToken.None);

            receipt.Should().NotBeNull();
            receipt!.OperationToken.Should().Be("token-verified");
            receipt.ActionId.Should().Be("spawn_context_entity");
            receipt.HelperEntryPoint.Should().Be("SWFOC_Trainer_Spawn_Context");
            receipt.Applied.Should().BeTrue();
            receipt.StageState.Should().Be("applied");
            receipt.VerifyState.Should().Be("receipt_present");
            receipt.ReasonCode.Should().Be("overlay_receipt_applied");
            receipt.VerificationSource.Should().Be("C:/Games/_LogFile.txt");
            receipt.AppliedEntityId.Should().Be("EMP_ATAT");
            receipt.ReceiptPath.Should().Be(receiptPath);
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task TryReadReceiptAsync_ShouldReadRuntimeMirrorReceipt_WithoutRedeployingFromStaleSourceTransport()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var gameRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "corruption"));
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")]),
                sourceRoot,
                installRoot,
                gameRootCandidates: [gameRoot]);
            var transport = (IHelperCommandTransportService)service;

            var runtimeRoot = Path.Combine(gameRoot, "corruption", "Mods", "SwfocTrainer_Helper", "base_swfoc");
            CreateTransportDirectories(runtimeRoot);
            var receiptPath = Path.Combine(runtimeRoot, "SwfocTrainer", "Runtime", "receipts", "token-runtime.json");
            await File.WriteAllTextAsync(
                receiptPath,
                """
                {
                  "operationToken": "token-runtime",
                  "actionId": "spawn_context_entity",
                  "helperEntryPoint": "SWFOC_Trainer_Spawn_Context",
                  "status": "applied",
                  "helperVerifyState": "receipt_present",
                  "reasonCode": "overlay_receipt_applied",
                  "message": "Runtime mirror applied command.",
                  "verificationSource": "C:/Games/_LogFile.txt",
                  "appliedEntityId": "EMP_ATAT"
                }
                """);

            var receipt = await transport.TryReadReceiptAsync("base_swfoc", "token-runtime", CancellationToken.None);

            receipt.Should().NotBeNull();
            receipt!.ReceiptPath.Should().Be(receiptPath);
            receipt.Message.Should().Be("Runtime mirror applied command.");
            receipt.Applied.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(gameRoot);
        }
    }

    [Fact]
    public async Task TryReadClaimAsync_ShouldReadRuntimeMirrorClaim_WithoutRedeployingFromStaleSourceTransport()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var gameRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "corruption"));
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")]),
                sourceRoot,
                installRoot,
                gameRootCandidates: [gameRoot]);
            var transport = (IHelperCommandTransportService)service;

            var runtimeRoot = Path.Combine(gameRoot, "corruption", "Mods", "SwfocTrainer_Helper", "base_swfoc");
            CreateTransportDirectories(runtimeRoot);
            var claimPath = Path.Combine(runtimeRoot, "SwfocTrainer", "Runtime", "commands", "claimed", "token-claim.json");
            await File.WriteAllTextAsync(
                claimPath,
                """
                {
                  "operationToken": "token-claim",
                  "actionId": "spawn_context_entity",
                  "helperEntryPoint": "SWFOC_Trainer_Spawn_Context",
                  "stageState": "claimed",
                  "message": "Runtime mirror claimed command."
                }
                """);

            var claim = await transport.TryReadClaimAsync("base_swfoc", "token-claim", CancellationToken.None);

            claim.Should().NotBeNull();
            claim!.ClaimPath.Should().Be(claimPath);
            claim.StageState.Should().Be("claimed");
            claim.Message.Should().Be("Runtime mirror claimed command.");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(gameRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldClearStaleTransportArtifacts_FromSourceAndRuntimeRoots()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var gameRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "corruption"));
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")]),
                sourceRoot,
                installRoot,
                gameRootCandidates: [gameRoot]);

            var sourceDeploymentRoot = Path.Combine(installRoot, "base_swfoc");
            CreateTransportDirectories(sourceDeploymentRoot);
            File.WriteAllText(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "commands", "dispatch.lua"), "return { stale = true }");
            File.WriteAllText(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "commands", "pending", "stale.json"), "{}");
            File.WriteAllText(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "commands", "claimed", "stale.json"), "{}");
            File.WriteAllText(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "receipts", "stale.json"), "{}");

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);

            var runtimeDispatchPath = Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "dispatch.lua");
            File.Exists(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "commands", "dispatch.lua")).Should().BeFalse();
            Directory.GetFiles(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "commands", "pending")).Should().BeEmpty();
            Directory.GetFiles(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "commands", "claimed")).Should().BeEmpty();
            Directory.GetFiles(Path.Combine(sourceDeploymentRoot, "SwfocTrainer", "Runtime", "receipts")).Should().BeEmpty();
            File.Exists(runtimeDispatchPath).Should().BeFalse();
            Directory.GetFiles(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "pending")).Should().BeEmpty();
            Directory.GetFiles(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "claimed")).Should().BeEmpty();
            Directory.GetFiles(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "receipts")).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(gameRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldGenerateAutoloadWrappers_WhenProfileDeclaresHelperAutoloadScripts()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var originalScriptsRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(originalScriptsRoot, "Story/Galactic.lua", "-- original galactic");
            WriteScript(originalScriptsRoot, "Story/LandBattle.lua", "-- original land");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Story/Galactic.lua,Story/LandBattle.lua"
            };
            var profile = BuildProfile(
                "base_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata);
            var service = BuildService(profile, sourceRoot, installRoot, originalScriptSearchRoots: [originalScriptsRoot]);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);

            var galacticWrapper = Path.Combine(deployedRoot, "Data", "Scripts", "Story", "Galactic.lua");
            var galacticOriginal = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer", "Original", "Story", "Galactic.lua");
            var landWrapper = Path.Combine(deployedRoot, "Data", "Scripts", "Story", "LandBattle.lua");
            var landOriginal = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer", "Original", "Story", "LandBattle.lua");

            File.Exists(galacticWrapper).Should().BeTrue();
            File.Exists(galacticOriginal).Should().BeTrue();
            File.Exists(landWrapper).Should().BeTrue();
            File.Exists(landOriginal).Should().BeTrue();

            var wrapper = File.ReadAllText(galacticWrapper);
            wrapper.Should().Contain("require(\"SwfocTrainer_HelperBootstrap\")");
            wrapper.Should().Contain("SwfocTrainer_Helper_Bootstrap_Pump()");
            wrapper.Should().Contain("require(\"SwfocTrainer.Original.Story.Galactic\")");
            wrapper.Should().Contain("SWFOC_TRAINER_HELPER_AUTOLOAD_READY");
            File.ReadAllText(galacticOriginal).Should().Be("-- original galactic");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(originalScriptsRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldWriteManifest_WithActivationScripts_WhenAutoloadWrappersAreGenerated()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var originalScriptsRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(originalScriptsRoot, "Story/SpaceBattle.lua", "-- original space");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Story/SpaceBattle.lua",
                ["helperAutoloadStrategy"] = "story_wrapper_chain"
            };
            var profile = BuildProfile(
                "base_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata);
            var service = BuildService(profile, sourceRoot, installRoot, originalScriptSearchRoots: [originalScriptsRoot]);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            var manifestPath = Path.Combine(deployedRoot, "helper-deployment.json");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

            var root = document.RootElement;
            root.GetProperty("activationStrategy").GetString().Should().Be("story_wrapper_chain");
            var activationScripts = root.GetProperty("activationScripts");
            activationScripts.GetArrayLength().Should().Be(1);
            var activation = activationScripts[0];
            activation.GetProperty("script").GetString().Should().Be("Story/SpaceBattle.lua");
            activation.GetProperty("deployedScript").GetString().Should().Be("Data/Scripts/Story/SpaceBattle.lua");
            activation.GetProperty("originalCopy").GetString().Should().Be("Data/Scripts/Library/SwfocTrainer/Original/Story/SpaceBattle.lua");
            activation.GetProperty("originalSourcePath").GetString().Should().EndWith("Story\\SpaceBattle.lua");
            activation.GetProperty("bootstrapRequirePath").GetString().Should().Be("SwfocTrainer_HelperBootstrap");
            activation.GetProperty("originalRequirePath").GetString().Should().Be("SwfocTrainer.Original.Story.SpaceBattle");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(originalScriptsRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldResolveLibraryAutoloadWrapper_FromWorkshopContentRoot_WhenProfileHasWorkshopId()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var workshopContentRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(workshopContentRoot, "1397421866/Data/Scripts/Library/PGStoryMode.lua", "-- original pgstorymode");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Library/PGStoryMode.lua",
                ["helperAutoloadStrategy"] = "story_wrapper_chain"
            };
            var profile = BuildProfile(
                "aotr_1397421866_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata,
                steamWorkshopId: "1397421866");
            var service = BuildService(profile, sourceRoot, installRoot, workshopContentRoots: [workshopContentRoot]);

            var deployedRoot = await service.DeployAsync("aotr_1397421866_swfoc", CancellationToken.None);

            var wrapperPath = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "PGStoryMode.lua");
            var originalCopyPath = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer", "Original", "Library", "PGStoryMode.lua");
            File.Exists(wrapperPath).Should().BeTrue();
            File.Exists(originalCopyPath).Should().BeTrue();
            File.ReadAllText(wrapperPath).Should().Contain("require(\"SwfocTrainer_HelperBootstrap\")");
            File.ReadAllText(wrapperPath).Should().Contain("require(\"SwfocTrainer.Original.Library.PGStoryMode\")");
            File.ReadAllText(originalCopyPath).Should().Be("-- original pgstorymode");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(workshopContentRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldWrapPumpEvents_ForLibraryPgBaseAutoloadScripts()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var workshopContentRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(workshopContentRoot, "1397421866/Data/Scripts/Library/PGBase.lua", "-- original pgbase");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Library/PGBase.lua",
                ["helperAutoloadStrategy"] = "service_wrapper_chain"
            };
            var profile = BuildProfile(
                "aotr_1397421866_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata,
                steamWorkshopId: "1397421866");
            var service = BuildService(profile, sourceRoot, installRoot, workshopContentRoots: [workshopContentRoot]);

            var deployedRoot = await service.DeployAsync("aotr_1397421866_swfoc", CancellationToken.None);

            var wrapperPath = Path.Combine(deployedRoot, "Data", "Scripts", "Library", "PGBase.lua");
            File.Exists(wrapperPath).Should().BeTrue();
            var wrapper = File.ReadAllText(wrapperPath);
            wrapper.Should().Contain("require(\"SwfocTrainer_HelperBootstrap\")");
            wrapper.Should().Contain("local original_PumpEvents = PumpEvents");
            wrapper.Should().Contain("function PumpEvents(...)");
            wrapper.Should().Contain("SwfocTrainer_Helper_Safe_Pump()");
            wrapper.Should().Contain("return original_PumpEvents(...)");
            wrapper.Should().Contain("require(\"SwfocTrainer.Original.Library.PGBase\")");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(workshopContentRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldMirrorHelperOverlayIntoGameCorruptionMods_WhenGameRootIsAvailable()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var workshopContentRoot = CreateTempDirectory();
        var gameRoot = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(gameRoot, "corruption"));
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(workshopContentRoot, "1397421866/Data/Scripts/Library/PGBase.lua", "-- original pgbase");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Library/PGBase.lua",
                ["helperAutoloadStrategy"] = "service_wrapper_chain"
            };
            var profile = BuildProfile(
                "aotr_1397421866_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata,
                steamWorkshopId: "1397421866");
            var service = BuildService(
                profile,
                sourceRoot,
                installRoot,
                workshopContentRoots: [workshopContentRoot],
                gameRootCandidates: [gameRoot]);

            var deployedRoot = await service.DeployAsync("aotr_1397421866_swfoc", CancellationToken.None);

            var sourceDeployment = Path.Combine(installRoot, "aotr_1397421866_swfoc");
            var mirroredDeployment = Path.Combine(gameRoot, "corruption", "Mods", "SwfocTrainer_Helper", "aotr_1397421866_swfoc");

            deployedRoot.Should().Be(mirroredDeployment);
            File.Exists(Path.Combine(sourceDeployment, "helper-deployment.json")).Should().BeTrue();
            File.Exists(Path.Combine(mirroredDeployment, "helper-deployment.json")).Should().BeTrue();
            File.Exists(Path.Combine(mirroredDeployment, "Data", "Scripts", "Library", "PGBase.lua")).Should().BeTrue();
            File.Exists(Path.Combine(mirroredDeployment, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua")).Should().BeTrue();
            var sourceBootstrap = File.ReadAllText(Path.Combine(sourceDeployment, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"));
            var mirroredBootstrap = File.ReadAllText(Path.Combine(mirroredDeployment, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"));
            sourceBootstrap.Should().Contain(NormalizeLuaPath(Path.Combine(sourceDeployment, "SwfocTrainer", "Runtime", "commands", "dispatch.lua")));
            mirroredBootstrap.Should().Contain(NormalizeLuaPath(Path.Combine(mirroredDeployment, "SwfocTrainer", "Runtime", "commands", "dispatch.lua")));
            mirroredBootstrap.Should().NotContain(NormalizeLuaPath(Path.Combine(sourceDeployment, "SwfocTrainer", "Runtime", "commands", "dispatch.lua")));

            var transportLayout = await ((IHelperCommandTransportService)service).GetLayoutAsync("aotr_1397421866_swfoc", CancellationToken.None);
            transportLayout.DeploymentRoot.Should().Be(mirroredDeployment);
            transportLayout.PendingDirectory.Should().StartWith(mirroredDeployment);
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(workshopContentRoot);
            DeleteDirectory(gameRoot);
        }
    }

    [Fact]
    public async Task GetLayoutAsync_ShouldRedeploy_WhenExistingActivationWrapperNoLongerMatchesProfile()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var originalScriptsRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(originalScriptsRoot, "Story/Galactic.lua", "-- original galactic");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Story/Galactic.lua",
                ["helperAutoloadStrategy"] = "story_wrapper_chain"
            };
            var profile = BuildProfile(
                "base_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata);
            var deployedRoot = Path.Combine(installRoot, "base_swfoc");
            Directory.CreateDirectory(Path.Combine(deployedRoot, "Data", "Scripts", "Library", "common"));
            Directory.CreateDirectory(Path.Combine(deployedRoot, "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(deployedRoot, "Data", "Scripts", "Library", "common", "spawn_bridge.lua"), "-- helper script");
            File.WriteAllText(Path.Combine(deployedRoot, "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- stale bootstrap");
            CreateTransportDirectories(deployedRoot);
            File.WriteAllText(Path.Combine(deployedRoot, "helper-deployment.json"), """{"profileId":"base_swfoc"}""");

            var service = BuildService(profile, sourceRoot, installRoot, originalScriptSearchRoots: [originalScriptsRoot]);

            var layout = await ((IHelperCommandTransportService)service).GetLayoutAsync("base_swfoc", CancellationToken.None);

            layout.ProfileId.Should().Be("base_swfoc");
            File.Exists(Path.Combine(deployedRoot, "Data", "Scripts", "Story", "Galactic.lua")).Should().BeTrue();
            File.ReadAllText(Path.Combine(deployedRoot, "Data", "Scripts", "Story", "Galactic.lua"))
                .Should().Contain("SWFOC_TRAINER_HELPER_AUTOLOAD_READY");
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(originalScriptsRoot);
        }
    }

    [Fact]
    public async Task DeployAsync_ShouldThrow_WhenHookSourceMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "missing.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var act = () => service.DeployAsync("base_swfoc", CancellationToken.None);

            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenHookFileMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenDeploymentReportIsMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0")]);
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "-- deployed script");
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            CreateTransportDirectories(Path.Combine(installRoot, "base_swfoc"));
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnTrue_WhenHookExistsWithoutHashMetadata()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var script = "common/spawn_bridge.lua";
            var profile = BuildProfile("base_swfoc", [new HelperHookSpec("spawn", script, "1.0.0")]);
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "-- deployed script");
            Directory.CreateDirectory(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            CreateTransportDirectories(Path.Combine(installRoot, "base_swfoc"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "helper-deployment.json"), """{"profileId":"base_swfoc"}""");
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenHashDoesNotMatch()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var script = "common/spawn_bridge.lua";
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha256"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            };
            var hook = new HelperHookSpec("spawn", script, "1.0.0", Metadata: metadata);
            var profile = BuildProfile("base_swfoc", [hook]);
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, "-- deployed script");
            Directory.CreateDirectory(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            CreateTransportDirectories(Path.Combine(installRoot, "base_swfoc"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "helper-deployment.json"), """{"profileId":"base_swfoc"}""");
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnTrue_WhenHashMatches()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            var content = "-- deployed script";
            var targetPath = Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "common", "spawn_bridge.lua");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, content);
            Directory.CreateDirectory(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "Data", "Scripts", "Library", "SwfocTrainer_HelperBootstrap.lua"), "-- bootstrap");
            CreateTransportDirectories(Path.Combine(installRoot, "base_swfoc"));
            File.WriteAllText(Path.Combine(installRoot, "base_swfoc", "helper-deployment.json"), """{"profileId":"base_swfoc"}""");
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)))
                .ToLowerInvariant();

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha256"] = sha
            };
            var hook = new HelperHookSpec("spawn", "common/spawn_bridge.lua", "1.0.0", Metadata: metadata);
            var profile = BuildProfile("base_swfoc", [hook]);
            var service = BuildService(profile, sourceRoot, installRoot);

            var verified = await service.VerifyAsync("base_swfoc");

            verified.Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenAutoloadWrapperIsMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        var originalScriptsRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            WriteScript(originalScriptsRoot, "Story/Galactic.lua", "-- original galactic");
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperAutoloadScripts"] = "Story/Galactic.lua"
            };
            var profile = BuildProfile(
                "base_swfoc",
                [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")],
                metadata);
            var service = BuildService(profile, sourceRoot, installRoot, originalScriptSearchRoots: [originalScriptsRoot]);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            File.Delete(Path.Combine(deployedRoot, "Data", "Scripts", "Story", "Galactic.lua"));

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
            DeleteDirectory(originalScriptsRoot);
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldReturnFalse_WhenTransportDirectoryMissing()
    {
        var sourceRoot = CreateTempDirectory();
        var installRoot = CreateTempDirectory();
        try
        {
            WriteScript(sourceRoot, "scripts/common/spawn_bridge.lua", "-- helper script");
            var service = BuildService(
                BuildProfile("base_swfoc", [new HelperHookSpec("spawn_bridge", "scripts/common/spawn_bridge.lua", "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn")]),
                sourceRoot,
                installRoot);

            var deployedRoot = await service.DeployAsync("base_swfoc", CancellationToken.None);
            Directory.Delete(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "claimed"), recursive: true);

            var verified = await service.VerifyAsync("base_swfoc", CancellationToken.None);

            verified.Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(sourceRoot);
            DeleteDirectory(installRoot);
        }
    }

    private static HelperModService BuildService(
        TrainerProfile profile,
        string sourceRoot,
        string installRoot,
        IReadOnlyList<string>? originalScriptSearchRoots = null,
        IReadOnlyList<string>? workshopContentRoots = null,
        IReadOnlyList<string>? gameRootCandidates = null)
    {
        var repository = new StubProfileRepository(profile);
        var options = new HelperModOptions
        {
            SourceRoot = sourceRoot,
            InstallRoot = installRoot,
            OriginalScriptSearchRoots = originalScriptSearchRoots ?? Array.Empty<string>(),
            WorkshopContentRoots = workshopContentRoots ?? Array.Empty<string>(),
            GameRootCandidates = gameRootCandidates ?? Array.Empty<string>()
        };
        return new HelperModService(repository, options, NullLogger<HelperModService>.Instance);
    }

    private static TrainerProfile BuildProfile(
        string profileId,
        IReadOnlyList<HelperHookSpec> hooks,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? steamWorkshopId = null)
    {
        return new TrainerProfile(
            Id: profileId,
            DisplayName: profileId,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: steamWorkshopId,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save-schema",
            HelperModHooks: hooks,
            Metadata: metadata);
    }

    private static string WriteScript(string sourceRoot, string relativePath, string content)
    {
        var fullPath = Path.Combine(sourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "swfoctrainer-helper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NormalizeLuaPath(string path) => path.Replace('\\', '/');

    private static void CreateTransportDirectories(string deployedRoot)
    {
        Directory.CreateDirectory(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "pending"));
        Directory.CreateDirectory(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "commands", "claimed"));
        Directory.CreateDirectory(Path.Combine(deployedRoot, "SwfocTrainer", "Runtime", "receipts"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public StubProfileRepository(TrainerProfile profile)
        {
            _profile = profile;
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotSupportedException();
        }
    }
}
