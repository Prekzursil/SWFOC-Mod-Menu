#pragma warning disable S4136
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Helper.Config;

namespace SwfocTrainer.Helper.Services;

public sealed class HelperModService : IHelperModService, IHelperCommandTransportService
{
    private const string DeploymentManifestFileName = "helper-deployment.json";
    private const string BootstrapScriptName = "SwfocTrainer_HelperBootstrap.lua";
    private const string BootstrapRequirePath = "SwfocTrainer_HelperBootstrap";
    private const string CommandTransportSchemaVersion = "1.0";
    private const string CommandTransportModel = "overlay_command_inbox";
    private const string AutoloadStrategyMetadataKey = "helperAutoloadStrategy";
    private const string AutoloadScriptsMetadataKey = "helperAutoloadScripts";
    private const string DefaultAutoloadStrategy = "story_wrapper_chain";
    private const string OriginalScriptCopyRoot = "SwfocTrainer/Original";
    private const string RuntimeTransportRoot = "SwfocTrainer/Runtime";
    private const string PendingCommandRoot = "SwfocTrainer/Runtime/commands/pending";
    private const string DispatchCommandRelativePath = "SwfocTrainer/Runtime/commands/dispatch.lua";
    private const string ClaimedCommandRoot = "SwfocTrainer/Runtime/commands/claimed";
    private const string ReceiptRoot = "SwfocTrainer/Runtime/receipts";
    private const string LaunchMirrorRootName = "SwfocTrainer_Helper";

    private readonly IProfileRepository _profiles;
    private readonly HelperModOptions _options;
    private readonly ILogger<HelperModService> _logger;

    public HelperModService(IProfileRepository profiles, HelperModOptions options, ILogger<HelperModService> logger)
    {
        _profiles = profiles;
        _options = options;
        _logger = logger;
    }

    public async Task<string> DeployAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        var sourceRoot = GetSourceDeploymentRoot(profileId);
        Directory.CreateDirectory(sourceRoot);
        ResetTransportState(sourceRoot);
        Directory.CreateDirectory(GetLibraryRoot(sourceRoot));

        var deployedScripts = new List<string>();
        var deployedHookManifests = new List<object>();

        foreach (var hook in profile.HelperModHooks)
        {
            var script = hook.Script;
            var sourcePath = Path.Combine(_options.SourceRoot, script);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Helper hook source not found: {sourcePath}");
            }

            var destination = GetDeployedHookScriptPath(sourceRoot, script);
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(sourcePath, destination, overwrite: true);
            var relativeDeployedScript = Path.GetRelativePath(sourceRoot, destination).Replace('\\', '/');
            deployedScripts.Add(relativeDeployedScript);
            deployedHookManifests.Add(new
            {
                id = hook.Id,
                script = hook.Script.Replace('\\', '/'),
                deployedScript = relativeDeployedScript,
                requirePath = NormalizeHookLuaRequirePath(hook.Script),
                entryPoint = hook.EntryPoint ?? string.Empty,
                version = hook.Version,
                sha256 = ComputeSha256(destination)
            });
        }

        var bootstrapPath = Path.Combine(GetLibraryRoot(sourceRoot), BootstrapScriptName);
        File.WriteAllText(bootstrapPath, BuildBootstrapScript(sourceRoot, profile.Id, profile.HelperModHooks));

        var activationStrategy = ResolveActivationStrategy(profile);
        var activationScripts = await DeployActivationScriptsAsync(sourceRoot, profile, activationStrategy, cancellationToken);

        File.WriteAllText(
            Path.Combine(sourceRoot, DeploymentManifestFileName),
            BuildDeploymentManifest(
                profile.Id,
                deployedScripts,
                Path.GetRelativePath(sourceRoot, bootstrapPath).Replace('\\', '/'),
                activationStrategy,
                activationScripts,
                BuildCommandTransportManifest(),
                deployedHookManifests));

        var runtimeRoot = ResolveRuntimeDeploymentRoot(profileId);
        if (!runtimeRoot.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            MirrorDeployment(sourceRoot, runtimeRoot);
            File.WriteAllText(
                Path.Combine(GetLibraryRoot(runtimeRoot), BootstrapScriptName),
                BuildBootstrapScript(runtimeRoot, profile.Id, profile.HelperModHooks));
        }

        _logger.LogInformation(
            "Deployed helper hooks for {ProfileId} into {TargetRoot} (runtimeRoot={RuntimeRoot}, activationStrategy={ActivationStrategy}, activationScripts={ActivationCount})",
            profileId,
            sourceRoot,
            runtimeRoot,
            activationStrategy,
            activationScripts.Count);

        return runtimeRoot;
    }

    public async Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        var sourceRoot = GetSourceDeploymentRoot(profileId);
        if (!VerifyDeploymentRoot(profile, sourceRoot))
        {
            return false;
        }

        var runtimeRoot = ResolveRuntimeDeploymentRoot(profileId);
        if (!runtimeRoot.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase) &&
            !VerifyDeploymentRoot(profile, runtimeRoot))
        {
            return false;
        }

        return true;
    }

    private bool VerifyDeploymentRoot(TrainerProfile profile, string targetRoot)
    {
        if (!File.Exists(Path.Combine(targetRoot, DeploymentManifestFileName)))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(GetLibraryRoot(targetRoot), BootstrapScriptName)))
        {
            return false;
        }

        if (!TransportDirectoriesExist(targetRoot))
        {
            return false;
        }

        foreach (var hook in profile.HelperModHooks)
        {
            var path = GetDeployedHookScriptPath(targetRoot, hook.Script);
            if (!File.Exists(path))
            {
                return false;
            }

            if (hook.Metadata is null || !hook.Metadata.TryGetValue("sha256", out var expectedSha) || string.IsNullOrWhiteSpace(expectedSha))
            {
                continue;
            }

            var actualSha = ComputeSha256(path);
            if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Helper hook hash mismatch for {HookId}: expected {Expected}, got {Actual}", hook.Id, expectedSha, actualSha);
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(ResolveActivationStrategy(profile)))
        {
            foreach (var activationScript in ResolveAutoloadScripts(profile))
            {
                if (!File.Exists(GetActivationWrapperPath(targetRoot, activationScript)) ||
                    !File.Exists(GetOriginalCopyPath(targetRoot, activationScript)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public Task<string> DeployAsync(string profileId)
    {
        return DeployAsync(profileId, CancellationToken.None);
    }

    public Task<bool> VerifyAsync(string profileId)
    {
        return VerifyAsync(profileId, CancellationToken.None);
    }

    public async Task<HelperCommandTransportLayout> GetLayoutAsync(string profileId, CancellationToken cancellationToken)
    {
        var deployedRoot = await EnsureDeploymentRootAsync(profileId, cancellationToken);
        return new HelperCommandTransportLayout(
            ProfileId: profileId,
            DeploymentRoot: deployedRoot,
            ManifestPath: Path.Combine(deployedRoot, DeploymentManifestFileName),
            BootstrapScriptPath: Path.Combine(GetLibraryRoot(deployedRoot), BootstrapScriptName),
            Model: CommandTransportModel,
            SchemaVersion: CommandTransportSchemaVersion,
            DispatchCommandPath: GetDispatchCommandPath(deployedRoot),
            PendingDirectory: GetPendingCommandRoot(deployedRoot),
            ClaimedDirectory: GetClaimedCommandRoot(deployedRoot),
            ReceiptDirectory: GetReceiptRoot(deployedRoot));
    }

    public async Task<HelperStagedCommand> StageCommandAsync(
        string profileId,
        string actionId,
        string helperEntryPoint,
        string operationToken,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(actionId))
        {
            throw new ArgumentException("Action id is required.", nameof(actionId));
        }

        if (string.IsNullOrWhiteSpace(helperEntryPoint))
        {
            throw new ArgumentException("Helper entry point is required.", nameof(helperEntryPoint));
        }

        if (string.IsNullOrWhiteSpace(operationToken))
        {
            throw new ArgumentException("Operation token is required.", nameof(operationToken));
        }

        ArgumentNullException.ThrowIfNull(payload);

        var layout = await GetLayoutAsync(profileId, cancellationToken);
        Directory.CreateDirectory(layout.PendingDirectory);
        Directory.CreateDirectory(layout.ClaimedDirectory);
        Directory.CreateDirectory(layout.ReceiptDirectory);

        var safeToken = operationToken.Trim();
        var commandPath = Path.Combine(layout.PendingDirectory, $"{safeToken}.json");
        var claimPath = Path.Combine(layout.ClaimedDirectory, $"{safeToken}.json");
        var receiptPath = Path.Combine(layout.ReceiptDirectory, $"{safeToken}.json");
        var dispatchPath = layout.DispatchCommandPath;

        if (File.Exists(commandPath))
        {
            File.Delete(commandPath);
        }

        if (File.Exists(claimPath))
        {
            File.Delete(claimPath);
        }

        if (File.Exists(receiptPath))
        {
            File.Delete(receiptPath);
        }

        if (File.Exists(dispatchPath))
        {
            File.Delete(dispatchPath);
        }

        payload["helperEntryPoint"] ??= helperEntryPoint;
        payload["operationToken"] ??= safeToken;

        var envelope = new JsonObject
        {
            ["schemaVersion"] = CommandTransportSchemaVersion,
            ["transportModel"] = CommandTransportModel,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["profileId"] = profileId,
            ["actionId"] = actionId,
            ["helperEntryPoint"] = helperEntryPoint,
            ["operationToken"] = safeToken,
            ["payload"] = payload.DeepClone()
        };

        await File.WriteAllTextAsync(
            commandPath,
            envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        await File.WriteAllTextAsync(
            dispatchPath,
            BuildDispatchCommandScript(profileId, actionId, helperEntryPoint, safeToken, payload),
            cancellationToken);

        _logger.LogInformation(
            "Staged helper overlay command for {ProfileId}: action={ActionId}, entryPoint={EntryPoint}, token={OperationToken}",
            profileId,
            actionId,
            helperEntryPoint,
            safeToken);

        return new HelperStagedCommand(
            ProfileId: profileId,
            ActionId: actionId,
            HelperEntryPoint: helperEntryPoint,
            OperationToken: safeToken,
            CommandPath: commandPath,
            ClaimPath: claimPath,
            ReceiptPath: receiptPath,
            PayloadPath: dispatchPath);
    }

    public async Task<HelperCommandReceipt?> TryReadReceiptAsync(
        string profileId,
        string operationToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(operationToken))
        {
            throw new ArgumentException("Operation token is required.", nameof(operationToken));
        }

        var receiptPath = ResolveTransportArtifactPath(profileId, operationToken.Trim(), ReceiptRoot);
        if (string.IsNullOrWhiteSpace(receiptPath) || !File.Exists(receiptPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(receiptPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        static string ReadString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        static bool ReadBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
                _ => false
            };
        }

        var status = ReadString(root, "status");
        var stageState = ReadString(root, "stageState");
        if (string.IsNullOrWhiteSpace(stageState))
        {
            stageState = status;
        }

        var verifyState = ReadString(root, "helperVerifyState");
        var verificationSource = ReadString(root, "verificationSource");
        var appliedEntityId = ReadString(root, "appliedEntityId");
        var actionId = ReadString(root, "actionId");
        var helperEntryPoint = ReadString(root, "helperEntryPoint");
        var reasonCode = ReadString(root, "reasonCode");
        var message = ReadString(root, "message");
        var effectiveOperationToken = ReadString(root, "operationToken");
        if (string.IsNullOrWhiteSpace(effectiveOperationToken))
        {
            effectiveOperationToken = operationToken.Trim();
        }

        var applied = ReadBool(root, "applied");
        if (!applied && !string.IsNullOrWhiteSpace(status))
        {
            applied = status.Equals("applied", StringComparison.OrdinalIgnoreCase) ||
                      status.Equals("verified", StringComparison.OrdinalIgnoreCase) ||
                      status.Equals("success", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(stageState))
        {
            stageState = applied ? "applied" : "receipt_present";
        }

        if (string.IsNullOrWhiteSpace(verifyState))
        {
            verifyState = applied ? "receipt_present" : "receipt_failed";
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            reasonCode = applied ? "overlay_receipt_applied" : "overlay_receipt_failed";
        }

        return new HelperCommandReceipt(
            ProfileId: profileId,
            ActionId: actionId,
            HelperEntryPoint: helperEntryPoint,
            OperationToken: effectiveOperationToken,
            ReceiptPath: receiptPath,
            StageState: stageState,
            Applied: applied,
            ReasonCode: reasonCode,
            Message: message,
            VerificationSource: verificationSource,
            VerifyState: verifyState,
            AppliedEntityId: appliedEntityId);
    }

    public async Task<HelperCommandClaim?> TryReadClaimAsync(
        string profileId,
        string operationToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(operationToken))
        {
            throw new ArgumentException("Operation token is required.", nameof(operationToken));
        }

        var claimPath = ResolveTransportArtifactPath(profileId, operationToken.Trim(), ClaimedCommandRoot);
        if (string.IsNullOrWhiteSpace(claimPath) || !File.Exists(claimPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(claimPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        static string ReadString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        var actionId = ReadString(root, "actionId");
        var helperEntryPoint = ReadString(root, "helperEntryPoint");
        var effectiveOperationToken = ReadString(root, "operationToken");
        if (string.IsNullOrWhiteSpace(effectiveOperationToken))
        {
            effectiveOperationToken = operationToken.Trim();
        }

        var stageState = ReadString(root, "stageState");
        if (string.IsNullOrWhiteSpace(stageState))
        {
            stageState = "claimed";
        }

        var message = ReadString(root, "message");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "Overlay command claimed.";
        }

        return new HelperCommandClaim(
            ProfileId: profileId,
            ActionId: actionId,
            HelperEntryPoint: helperEntryPoint,
            OperationToken: effectiveOperationToken,
            ClaimPath: claimPath,
            StageState: stageState,
            Message: message);
    }

    private Task<IReadOnlyList<ActivationScriptDeployment>> DeployActivationScriptsAsync(
        string targetRoot,
        TrainerProfile profile,
        string activationStrategy,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var autoloadScripts = ResolveAutoloadScripts(profile);
        if (autoloadScripts.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ActivationScriptDeployment>>(Array.Empty<ActivationScriptDeployment>());
        }

        var searchRoots = ResolveOriginalScriptSearchRoots(profile);
        var deployments = new List<ActivationScriptDeployment>(autoloadScripts.Count);

        foreach (var activationScript in autoloadScripts)
        {
            var normalizedScript = NormalizeDataScriptsRelativePath(activationScript);
            var originalSourcePath = ResolveOriginalScriptSourcePath(searchRoots, normalizedScript)
                ?? throw new FileNotFoundException(
                    $"Original activation script not found for helper autoload '{normalizedScript}'. " +
                    $"Search roots: {string.Join(", ", searchRoots)}");

            var originalCopyPath = GetOriginalCopyPath(targetRoot, normalizedScript);
            var originalCopyDirectory = Path.GetDirectoryName(originalCopyPath);
            if (!string.IsNullOrWhiteSpace(originalCopyDirectory))
            {
                Directory.CreateDirectory(originalCopyDirectory);
            }

            File.Copy(originalSourcePath, originalCopyPath, overwrite: true);

            var wrapperPath = GetActivationWrapperPath(targetRoot, normalizedScript);
            var wrapperDirectory = Path.GetDirectoryName(wrapperPath);
            if (!string.IsNullOrWhiteSpace(wrapperDirectory))
            {
                Directory.CreateDirectory(wrapperDirectory);
            }

            var originalRequirePath = NormalizeLuaRequirePath(Path.Combine(OriginalScriptCopyRoot, normalizedScript));
            File.WriteAllText(wrapperPath, BuildAutoloadWrapperScript(profile.Id, activationStrategy, normalizedScript, originalRequirePath));

            deployments.Add(new ActivationScriptDeployment(
                Script: normalizedScript.Replace('\\', '/'),
                DeployedScript: Path.GetRelativePath(targetRoot, wrapperPath).Replace('\\', '/'),
                OriginalCopy: Path.GetRelativePath(targetRoot, originalCopyPath).Replace('\\', '/'),
                OriginalSourcePath: originalSourcePath,
                BootstrapRequirePath: BootstrapRequirePath,
                OriginalRequirePath: originalRequirePath));
        }

        return Task.FromResult<IReadOnlyList<ActivationScriptDeployment>>(deployments);
    }

    private IReadOnlyList<string> ResolveOriginalScriptSearchRoots(TrainerProfile profile)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var explicitRoot in _options.OriginalScriptSearchRoots)
        {
            AddSearchRoot(roots, seen, explicitRoot);
        }

        if (roots.Count > 0)
        {
            return roots;
        }

        foreach (var workshopId in ResolveWorkshopChain(profile))
        {
            foreach (var workshopRoot in _options.WorkshopContentRoots)
            {
                AddSearchRoot(roots, seen, Path.Combine(workshopRoot, workshopId, "Data", "Scripts"));
            }
        }

        foreach (var gameRoot in _options.GameRootCandidates)
        {
            var scriptsRoot = profile.ExeTarget == ExeTarget.Sweaw
                ? Path.Combine(gameRoot, "GameData", "Data", "Scripts")
                : Path.Combine(gameRoot, "corruption", "Data", "Scripts");
            AddSearchRoot(roots, seen, scriptsRoot);
        }

        return roots;
    }

    private static IReadOnlyList<string> ResolveWorkshopChain(TrainerProfile profile)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddWorkshopIds(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(token))
                {
                    ordered.Add(token);
                }
            }
        }

        AddWorkshopIds(profile.SteamWorkshopId);
        if (profile.Metadata is not null)
        {
            profile.Metadata.TryGetValue("requiredWorkshopIds", out var requiredWorkshopIds);
            AddWorkshopIds(requiredWorkshopIds);
            profile.Metadata.TryGetValue("parentDependencies", out var parentDependencies);
            AddWorkshopIds(parentDependencies);
        }

        return ordered;
    }

    private static void AddSearchRoot(ICollection<string> roots, ISet<string> seen, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var normalized = candidate.Trim();
        if (Directory.Exists(normalized) && seen.Add(normalized))
        {
            roots.Add(normalized);
        }
    }

    private static string? ResolveOriginalScriptSourcePath(IReadOnlyList<string> searchRoots, string normalizedScript)
    {
        foreach (var root in searchRoots)
        {
            var candidate = Path.Combine(root, normalizedScript);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string GetLibraryRoot(string targetRoot)
    {
        return Path.Combine(targetRoot, "Data", "Scripts", "Library");
    }

    private static string GetDeployedHookScriptPath(string targetRoot, string script)
    {
        return Path.Combine(GetLibraryRoot(targetRoot), NormalizeHookRelativePath(script));
    }

    private static string GetActivationWrapperPath(string targetRoot, string script)
    {
        return Path.Combine(targetRoot, "Data", "Scripts", NormalizeDataScriptsRelativePath(script));
    }

    private static string GetOriginalCopyPath(string targetRoot, string script)
    {
        return Path.Combine(GetLibraryRoot(targetRoot), OriginalScriptCopyRoot, NormalizeDataScriptsRelativePath(script));
    }

    private static string NormalizeHookRelativePath(string script)
    {
        var normalized = NormalizeDataScriptsRelativePath(script);
        var scriptPrefix = $"scripts{Path.DirectorySeparatorChar}";
        if (normalized.StartsWith(scriptPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[scriptPrefix.Length..];
        }

        return normalized;
    }

    private static string NormalizeDataScriptsRelativePath(string script)
    {
        var normalized = script
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var dataScriptsPrefix = $"Data{Path.DirectorySeparatorChar}Scripts{Path.DirectorySeparatorChar}";
        if (normalized.StartsWith(dataScriptsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[dataScriptsPrefix.Length..];
        }

        return normalized;
    }

    private static string NormalizeHookLuaRequirePath(string script)
    {
        var relativePath = NormalizeHookRelativePath(script)
            .Replace(Path.DirectorySeparatorChar, '.');

        if (relativePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath[..^4];
        }

        return relativePath.Trim('.');
    }

    private static string NormalizeLuaRequirePath(string script)
    {
        var relativePath = NormalizeDataScriptsRelativePath(script)
            .Replace(Path.DirectorySeparatorChar, '.');

        if (relativePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath[..^4];
        }

        return relativePath.Trim('.');
    }

    private static string ResolveActivationStrategy(TrainerProfile profile)
    {
        if (profile.Metadata is null)
        {
            return string.Empty;
        }

        if (profile.Metadata.TryGetValue(AutoloadStrategyMetadataKey, out var explicitStrategy) &&
            !string.IsNullOrWhiteSpace(explicitStrategy))
        {
            return explicitStrategy.Trim();
        }

        return ResolveAutoloadScripts(profile).Count > 0 ? DefaultAutoloadStrategy : string.Empty;
    }

    private static IReadOnlyList<string> ResolveAutoloadScripts(TrainerProfile profile)
    {
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue(AutoloadScriptsMetadataKey, out var rawScripts) ||
            string.IsNullOrWhiteSpace(rawScripts))
        {
            return Array.Empty<string>();
        }

        return rawScripts
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeDataScriptsRelativePath)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildBootstrapScript(string targetRoot, string profileId, IReadOnlyList<HelperHookSpec> hooks)
    {
        var hookEntries = string.Join(Environment.NewLine, hooks.Select(BuildBootstrapHookEntry));
        var dispatchCommandPath = NormalizeLuaFilePath(GetDispatchCommandPath(targetRoot));
        var claimedRootPath = NormalizeLuaFilePath(GetClaimedCommandRoot(targetRoot));
        var receiptRootPath = NormalizeLuaFilePath(GetReceiptRoot(targetRoot));

        return $$"""
-- Auto-generated by SWFOC Trainer.
SWFOC_TRAINER_HELPER_PROFILE = "{{EscapeLuaString(profileId)}}"
SWFOC_TRAINER_HELPER_HOOK_COUNT = {{hooks.Count}}
SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT = "{{EscapeLuaString(CommandTransportModel)}}"
SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT_SCHEMA = "{{EscapeLuaString(CommandTransportSchemaVersion)}}"
SWFOC_TRAINER_HELPER_COMMAND_ROOT = "{{EscapeLuaString(RuntimeTransportRoot)}}"
SWFOC_TRAINER_HELPER_COMMAND_PENDING = "{{EscapeLuaString(PendingCommandRoot)}}"
SWFOC_TRAINER_HELPER_COMMAND_DISPATCH = "{{EscapeLuaString(dispatchCommandPath)}}"
SWFOC_TRAINER_HELPER_COMMAND_CLAIMED = "{{EscapeLuaString(ClaimedCommandRoot)}}"
SWFOC_TRAINER_HELPER_COMMAND_CLAIMED_ABS = "{{EscapeLuaString(claimedRootPath)}}"
SWFOC_TRAINER_HELPER_RECEIPT_ROOT = "{{EscapeLuaString(ReceiptRoot)}}"
SWFOC_TRAINER_HELPER_RECEIPT_ROOT_ABS = "{{EscapeLuaString(receiptRootPath)}}"
SWFOC_TRAINER_HELPER_HOOKS = {
{{hookEntries}}
}

local SWFOC_TRAINER_HELPER_LAST_COMMAND_TOKEN = nil

local function SwfocTrainer_Helper_Bootstrap_Output(message)
    if message == nil or message == "" then
        return
    end

    if _OuputDebug then
        pcall(function()
            _OuputDebug(message)
        end)
        return
    end

    if _OutputDebug then
        pcall(function()
            _OutputDebug(message)
        end)
    end
end

local function SwfocTrainer_Helper_Bootstrap_Has_Value(value)
    return value ~= nil and tostring(value) ~= ""
end

local function SwfocTrainer_Helper_Bootstrap_Escape_Json_String(value)
    local text = tostring(value or "")
    text = string.gsub(text, "\\", "\\\\")
    text = string.gsub(text, '"', '\\"')
    text = string.gsub(text, "\r", "\\r")
    text = string.gsub(text, "\n", "\\n")
    return text
end

local function SwfocTrainer_Helper_Bootstrap_Write_Text(path, content)
    if not io or not io.open or not SwfocTrainer_Helper_Bootstrap_Has_Value(path) then
        return false, "io_unavailable"
    end

    local file, open_error = io.open(path, "w")
    if not file then
        return false, tostring(open_error or "open_failed")
    end

    local ok, write_error = pcall(function()
        file:write(content or "")
    end)
    file:close()
    if not ok then
        return false, tostring(write_error)
    end

    return true, nil
end

local function SwfocTrainer_Helper_Bootstrap_Delete_File(path)
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(path) then
        return true, nil
    end

    if not os or not os.remove then
        return false, "remove_unavailable"
    end

    local ok, remove_error = pcall(function()
        return os.remove(path)
    end)
    if not ok then
        return false, tostring(remove_error)
    end

    return true, nil
end

function SwfocTrainer_Helper_Bootstrap_Describe()
    return {
        profile = SWFOC_TRAINER_HELPER_PROFILE,
        hookCount = SWFOC_TRAINER_HELPER_HOOK_COUNT,
        transport = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT,
        schemaVersion = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT_SCHEMA
    }
end

function SwfocTrainer_Helper_Bootstrap_DescribeTransport()
    return {
        model = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT,
        schemaVersion = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT_SCHEMA,
        root = SWFOC_TRAINER_HELPER_COMMAND_ROOT,
        pending = SWFOC_TRAINER_HELPER_COMMAND_PENDING,
        dispatch = SWFOC_TRAINER_HELPER_COMMAND_DISPATCH,
        claimed = SWFOC_TRAINER_HELPER_COMMAND_CLAIMED,
        claimedAbsolute = SWFOC_TRAINER_HELPER_COMMAND_CLAIMED_ABS,
        receipts = SWFOC_TRAINER_HELPER_RECEIPT_ROOT,
        receiptsAbsolute = SWFOC_TRAINER_HELPER_RECEIPT_ROOT_ABS
    }
end

local function SwfocTrainer_Helper_Bootstrap_Resolve_Command()
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(SWFOC_TRAINER_HELPER_COMMAND_DISPATCH) then
        return nil, "dispatch_path_missing"
    end

    local ok, loaded = pcall(dofile, SWFOC_TRAINER_HELPER_COMMAND_DISPATCH)
    if not ok then
        return nil, tostring(loaded)
    end

    if type(loaded) ~= "table" then
        return nil, "dispatch_command_invalid"
    end

    return loaded, nil
end

local function SwfocTrainer_Helper_Bootstrap_Resolve_Command_Payload(command)
    if type(command) ~= "table" then
        return nil
    end

    local payload = command["payload"]
    if type(payload) == "table" then
        return payload
    end

    return command
end

local function SwfocTrainer_Helper_Bootstrap_Resolve_Command_EntryPoint(command)
    if type(command) ~= "table" then
        return nil
    end

    local candidate = command["helperEntryPoint"]
    if candidate == nil or candidate == "" then
        candidate = command["entryPoint"]
    end

    return candidate
end

local function SwfocTrainer_Helper_Bootstrap_Resolve_Command_OperationToken(command)
    if type(command) ~= "table" then
        return ""
    end

    local operationToken = tostring(command["operationToken"] or command["operation_token"] or "")
    if SwfocTrainer_Helper_Bootstrap_Has_Value(operationToken) then
        return operationToken
    end

    local payload = SwfocTrainer_Helper_Bootstrap_Resolve_Command_Payload(command)
    if type(payload) == "table" then
        return tostring(payload["operationToken"] or payload["operation_token"] or "")
    end

    return ""
end

local function SwfocTrainer_Helper_Bootstrap_Resolve_Applied_Entity_Id(command)
    local payload = SwfocTrainer_Helper_Bootstrap_Resolve_Command_Payload(command)
    if type(payload) ~= "table" then
        return ""
    end

    local candidate = payload["appliedEntityId"] or payload["entityId"] or payload["unitId"] or payload["targetFaction"] or payload["globalKey"] or ""
    return tostring(candidate or "")
end

local function SwfocTrainer_Helper_Bootstrap_Resolve_Command_Args(command)
    if type(command) ~= "table" then
        return {}
    end

    local args = command["args"]
    if type(args) == "table" then
        return args
    end

    local payload = SwfocTrainer_Helper_Bootstrap_Resolve_Command_Payload(command)
    if type(payload) ~= "table" then
        return {}
    end

    local entryPoint = SwfocTrainer_Helper_Bootstrap_Resolve_Command_EntryPoint(command)
    local operationToken = SwfocTrainer_Helper_Bootstrap_Resolve_Command_OperationToken(command)
    local faction = payload["targetFaction"] or payload["faction"]

    if entryPoint == "SWFOC_Trainer_Spawn" then
        return { payload["unitId"] or payload["entityId"], payload["entryMarker"] or payload["worldPosition"], faction, operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Spawn_Context" then
        return {
            payload["entityId"],
            payload["unitId"],
            payload["entryMarker"] or payload["worldPosition"],
            faction,
            payload["targetContext"],
            operationToken,
            payload["operationPolicy"],
            payload["mutationIntent"],
            payload["placementMode"]
        }
    end

    if entryPoint == "SWFOC_Trainer_Place_Building" then
        return { payload["entityId"], payload["entryMarker"] or payload["worldPosition"], faction, payload["forceOverride"], operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Set_Context_Allegiance" then
        return { payload["entityId"], faction, payload["sourceFaction"], payload["targetContext"], payload["allowCrossFaction"], operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Transfer_Fleet_Safe" then
        return { payload["entityId"], payload["sourceFaction"], faction, payload["safePlanetId"] or payload["entryMarker"], payload["forceOverride"], operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Flip_Planet_Owner" then
        return { payload["entityId"], faction, payload["flipMode"], payload["forceOverride"], operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Switch_Player_Faction" then
        return { faction, operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Edit_Hero_State" then
        return { payload["entityId"], payload["globalKey"], payload["desiredState"] or payload["heroState"], payload["allowDuplicate"] or payload["forceOverride"], operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Create_Hero_Variant" then
        return { payload["sourceHeroId"] or payload["sourceEntityId"] or payload["entityId"], payload["variantHeroId"] or payload["unitId"], faction, operationToken }
    end

    if entryPoint == "SWFOC_Trainer_Set_Hero_Respawn" then
        return { payload["globalKey"], payload["intValue"] or payload["value"], operationToken }
    end

    return { payload }
end

local function SwfocTrainer_Helper_Bootstrap_Invoke_EntryPoint(entryPoint, args)
    if entryPoint == nil or entryPoint == "" then
        return false, "missing_entry_point"
    end

    local fn = _G[entryPoint]
    if type(fn) ~= "function" then
        return false, "missing_runtime_function"
    end

    local ok, result = pcall(function()
        return fn(table.unpack(args))
    end)
    if not ok then
        return false, tostring(result)
    end

    if result == nil then
        return true, "invoked"
    end

    return result and true or false, tostring(result)
end

local function SwfocTrainer_Helper_Bootstrap_Compose_Claim_Path(operationToken)
    return SWFOC_TRAINER_HELPER_COMMAND_CLAIMED_ABS .. "/" .. tostring(operationToken) .. ".json"
end

local function SwfocTrainer_Helper_Bootstrap_Compose_Receipt_Path(operationToken)
    return SWFOC_TRAINER_HELPER_RECEIPT_ROOT_ABS .. "/" .. tostring(operationToken) .. ".json"
end

local function SwfocTrainer_Helper_Bootstrap_Write_Claim(command, stageState, message)
    local operationToken = SwfocTrainer_Helper_Bootstrap_Resolve_Command_OperationToken(command)
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(operationToken) then
        return false, "missing_operation_token"
    end

    local claimPath = SwfocTrainer_Helper_Bootstrap_Compose_Claim_Path(operationToken)
    local body = "{\n" ..
        "  \"operationToken\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(operationToken) .. "\",\n" ..
        "  \"actionId\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(tostring(command[\"actionId\"] or "")) .. "\",\n" ..
        "  \"helperEntryPoint\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(tostring(command[\"helperEntryPoint\"] or "")) .. "\",\n" ..
        "  \"stageState\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(stageState or "claimed") .. "\",\n" ..
        "  \"message\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(message or "Overlay command claimed.") .. "\"\n" ..
        "}"

    return SwfocTrainer_Helper_Bootstrap_Write_Text(claimPath, body)
end

local function SwfocTrainer_Helper_Bootstrap_Write_Receipt(command, applied, reasonCode, message, stageState, verifyState, appliedEntityId)
    local operationToken = SwfocTrainer_Helper_Bootstrap_Resolve_Command_OperationToken(command)
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(operationToken) then
        return false, "missing_operation_token"
    end

    local receiptPath = SwfocTrainer_Helper_Bootstrap_Compose_Receipt_Path(operationToken)
    local effectiveVerifyState = verifyState
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(effectiveVerifyState) then
        effectiveVerifyState = applied and "receipt_present" or "receipt_failed"
    end

    local effectiveReasonCode = reasonCode
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(effectiveReasonCode) then
        effectiveReasonCode = applied and "overlay_receipt_applied" or "overlay_execution_failed"
    end

    local status = applied and "applied" or "failed"
    local body = "{\n" ..
        "  \"operationToken\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(operationToken) .. "\",\n" ..
        "  \"actionId\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(tostring(command[\"actionId\"] or "")) .. "\",\n" ..
        "  \"helperEntryPoint\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(tostring(command[\"helperEntryPoint\"] or "")) .. "\",\n" ..
        "  \"status\": \"" .. status .. "\",\n" ..
        "  \"stageState\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(stageState or status) .. "\",\n" ..
        "  \"applied\": " .. (applied and "true" or "false") .. ",\n" ..
        "  \"helperVerifyState\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(effectiveVerifyState) .. "\",\n" ..
        "  \"reasonCode\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(effectiveReasonCode) .. "\",\n" ..
        "  \"message\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(message or "") .. "\",\n" ..
        "  \"verificationSource\": \"_LogFile.txt\",\n" ..
        "  \"appliedEntityId\": \"" .. SwfocTrainer_Helper_Bootstrap_Escape_Json_String(appliedEntityId or "") .. "\"\n" ..
        "}"

    return SwfocTrainer_Helper_Bootstrap_Write_Text(receiptPath, body)
end

function SwfocTrainer_Helper_Bootstrap_Execute_Command(command)
    local entryPoint = SwfocTrainer_Helper_Bootstrap_Resolve_Command_EntryPoint(command)
    local args = SwfocTrainer_Helper_Bootstrap_Resolve_Command_Args(command)
    local operationToken = SwfocTrainer_Helper_Bootstrap_Resolve_Command_OperationToken(command)
    local appliedEntityId = SwfocTrainer_Helper_Bootstrap_Resolve_Applied_Entity_Id(command)

    local ok, detail = SwfocTrainer_Helper_Bootstrap_Invoke_EntryPoint(entryPoint, args)
    if ok then
        SwfocTrainer_Helper_Bootstrap_Output("SWFOC_TRAINER_HELPER_COMMAND_ACCEPTED profile=" .. SWFOC_TRAINER_HELPER_PROFILE .. " entry=" .. tostring(entryPoint) .. " token=" .. operationToken)
        return true, "overlay_receipt_applied", appliedEntityId, "Overlay command applied."
    end

    SwfocTrainer_Helper_Bootstrap_Output("SWFOC_TRAINER_HELPER_COMMAND_FAILED profile=" .. SWFOC_TRAINER_HELPER_PROFILE .. " entry=" .. tostring(entryPoint) .. " token=" .. operationToken .. " error=" .. tostring(detail))
    return false, tostring(detail or "overlay_execution_failed"), appliedEntityId, tostring(detail or "Overlay command failed.")
end

function SwfocTrainer_Helper_Bootstrap_Pump()
    local command, load_error = SwfocTrainer_Helper_Bootstrap_Resolve_Command()
    if command == nil then
        if SwfocTrainer_Helper_Bootstrap_Has_Value(load_error) and load_error ~= "dispatch_path_missing" then
            SwfocTrainer_Helper_Bootstrap_Output("SWFOC_TRAINER_HELPER_COMMAND_LOAD_FAILED profile=" .. SWFOC_TRAINER_HELPER_PROFILE .. " error=" .. tostring(load_error))
        end
        return false
    end

    local operationToken = SwfocTrainer_Helper_Bootstrap_Resolve_Command_OperationToken(command)
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(operationToken) then
        SwfocTrainer_Helper_Bootstrap_Output("SWFOC_TRAINER_HELPER_COMMAND_INVALID profile=" .. SWFOC_TRAINER_HELPER_PROFILE .. " reason=missing_operation_token")
        SwfocTrainer_Helper_Bootstrap_Delete_File(SWFOC_TRAINER_HELPER_COMMAND_DISPATCH)
        return false
    end

    if SWFOC_TRAINER_HELPER_LAST_COMMAND_TOKEN == operationToken then
        return false
    end

    SWFOC_TRAINER_HELPER_LAST_COMMAND_TOKEN = operationToken
    SwfocTrainer_Helper_Bootstrap_Write_Claim(command, "claimed", "Overlay command claimed.")
    local applied, reasonCode, appliedEntityId, detailMessage = SwfocTrainer_Helper_Bootstrap_Execute_Command(command)
    local receiptMessage = detailMessage
    if not SwfocTrainer_Helper_Bootstrap_Has_Value(receiptMessage) then
        receiptMessage = applied and "Overlay command applied." or "Overlay command failed."
    end
    SwfocTrainer_Helper_Bootstrap_Write_Receipt(command, applied, reasonCode, receiptMessage, applied and "applied" or "failed", applied and "receipt_present" or "receipt_failed", appliedEntityId)
    SwfocTrainer_Helper_Bootstrap_Delete_File(SWFOC_TRAINER_HELPER_COMMAND_DISPATCH)
    return applied
end

function SwfocTrainer_Helper_Bootstrap_LoadAll()
    local loaded = 0
    for _, hook in ipairs(SWFOC_TRAINER_HELPER_HOOKS) do
        local ok, module_or_error = pcall(require, hook.requirePath)
        if ok then
            loaded = loaded + 1
            SwfocTrainer_Helper_Bootstrap_Output("SWFOC_TRAINER_HELPER_BOOTSTRAP_LOADED profile=" .. SWFOC_TRAINER_HELPER_PROFILE .. " hook=" .. hook.id .. " require=" .. hook.requirePath)
        else
            SwfocTrainer_Helper_Bootstrap_Output("SWFOC_TRAINER_HELPER_BOOTSTRAP_FAILED profile=" .. SWFOC_TRAINER_HELPER_PROFILE .. " hook=" .. hook.id .. " require=" .. hook.requirePath .. " error=" .. tostring(module_or_error))
        end
    end

    return loaded
end

SwfocTrainer_Helper_Bootstrap_LoadAll()
SwfocTrainer_Helper_Bootstrap_Pump()
""" + Environment.NewLine;
    }

    private static string BuildBootstrapHookEntry(HelperHookSpec hook)
    {
        return $$"""
    {
        id = "{{EscapeLuaString(hook.Id)}}",
        script = "{{EscapeLuaString(hook.Script.Replace('\\', '/'))}}",
        requirePath = "{{EscapeLuaString(NormalizeHookLuaRequirePath(hook.Script))}}",
        entryPoint = "{{EscapeLuaString(hook.EntryPoint ?? string.Empty)}}",
        version = "{{EscapeLuaString(hook.Version)}}"
    },
""";
    }

    private static string BuildDispatchCommandScript(string profileId, string actionId, string helperEntryPoint, string operationToken, JsonObject payload)
    {
        return $$"""
return {
    ["schemaVersion"] = "{{EscapeLuaString(CommandTransportSchemaVersion)}}",
    ["transportModel"] = "{{EscapeLuaString(CommandTransportModel)}}",
    ["generatedAtUtc"] = "{{DateTimeOffset.UtcNow:O}}",
    ["profileId"] = "{{EscapeLuaString(profileId)}}",
    ["actionId"] = "{{EscapeLuaString(actionId)}}",
    ["helperEntryPoint"] = "{{EscapeLuaString(helperEntryPoint)}}",
    ["operationToken"] = "{{EscapeLuaString(operationToken)}}",
    ["payload"] = {{BuildLuaLiteral(payload, 1)}}
}
""" + Environment.NewLine;
    }

    private static string BuildLuaLiteral(JsonNode? node, int indentLevel = 0)
    {
        if (node is null)
        {
            return "nil";
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return $"\"{EscapeLuaString(stringValue)}\"";
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        var indent = new string(' ', indentLevel * 4);
        var childIndent = new string(' ', (indentLevel + 1) * 4);

        if (node is JsonArray array)
        {
            if (array.Count == 0)
            {
                return "{}";
            }

            var items = array.Select(item => $"{childIndent}{BuildLuaLiteral(item, indentLevel + 1)}");
            return "{\n" + string.Join(",\n", items) + "\n" + indent + "}";
        }

        if (node is JsonObject obj)
        {
            if (obj.Count == 0)
            {
                return "{}";
            }

            var properties = obj.Select(kvp => $"{childIndent}[\"{EscapeLuaString(kvp.Key)}\"] = {BuildLuaLiteral(kvp.Value, indentLevel + 1)}");
            return "{\n" + string.Join(",\n", properties) + "\n" + indent + "}";
        }

        return "nil";
    }

    private static string NormalizeLuaFilePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string BuildAutoloadWrapperScript(
        string profileId,
        string activationStrategy,
        string scriptPath,
        string originalRequirePath)
    {
        var normalizedScriptPath = scriptPath.Replace('\\', '/');
        var lines = new List<string>
        {
            "-- Auto-generated helper activation wrapper for SWFOC Trainer.",
            "local function SwfocTrainer_Helper_Autoload_Output(message)",
            "    if message == nil or message == \"\" then",
            "        return",
            "    end",
            string.Empty,
            "    if _OuputDebug then",
            "        pcall(function()",
            "            _OuputDebug(message)",
            "        end)",
            "        return",
            "    end",
            string.Empty,
            "    if _OutputDebug then",
            "        pcall(function()",
            "            _OutputDebug(message)",
            "        end)",
            "    end",
            "end",
            string.Empty,
            "local function SwfocTrainer_Helper_Safe_Pump()",
            "    if SwfocTrainer_Helper_Bootstrap_Pump then",
            "        pcall(function()",
            "            SwfocTrainer_Helper_Bootstrap_Pump()",
            "        end)",
            "    end",
            "end",
            string.Empty,
            "local bootstrap_ok, bootstrap_error = pcall(function()",
            $"    return require(\"{EscapeLuaString(BootstrapRequirePath)}\")",
            "end)",
            "if bootstrap_ok then",
            $"    SwfocTrainer_Helper_Autoload_Output(\"SWFOC_TRAINER_HELPER_AUTOLOAD_READY profile={EscapeLuaString(profileId)} strategy={EscapeLuaString(activationStrategy)} script={EscapeLuaString(normalizedScriptPath)}\")",
            "    SwfocTrainer_Helper_Safe_Pump()",
            "else",
            $"    SwfocTrainer_Helper_Autoload_Output(\"SWFOC_TRAINER_HELPER_AUTOLOAD_FAILED profile={EscapeLuaString(profileId)} strategy={EscapeLuaString(activationStrategy)} script={EscapeLuaString(normalizedScriptPath)} error=\" .. tostring(bootstrap_error))",
            "end",
            string.Empty,
            $"local original_module = require(\"{EscapeLuaString(originalRequirePath)}\")"
        };

        if (normalizedScriptPath.Equals("Library/PGBase.lua", StringComparison.OrdinalIgnoreCase))
        {
            lines.AddRange(
            [
                "local original_PumpEvents = PumpEvents",
                "if type(original_PumpEvents) == \"function\" then",
                "    function PumpEvents(...)",
                "        if bootstrap_ok then",
                "            SwfocTrainer_Helper_Safe_Pump()",
                "        end",
                "        return original_PumpEvents(...)",
                "    end",
                "end"
            ]);
        }
        else if (normalizedScriptPath.Equals("Library/PGStoryMode.lua", StringComparison.OrdinalIgnoreCase))
        {
            lines.AddRange(
            [
                "local original_PG_Story_State_Init = PG_Story_State_Init",
                "if type(original_PG_Story_State_Init) == \"function\" then",
                "    function PG_Story_State_Init(message)",
                "        if bootstrap_ok and (message == OnEnter or message == OnUpdate) then",
                "            SwfocTrainer_Helper_Safe_Pump()",
                "        end",
                "        return original_PG_Story_State_Init(message)",
                "    end",
                "end",
                string.Empty,
                "local original_Story_Event_Trigger = Story_Event_Trigger",
                "if type(original_Story_Event_Trigger) == \"function\" then",
                "    function Story_Event_Trigger(...)",
                "        if bootstrap_ok then",
                "            SwfocTrainer_Helper_Safe_Pump()",
                "        end",
                "        return original_Story_Event_Trigger(...)",
                "    end",
                "end"
            ]);
        }

        lines.Add(string.Empty);
        lines.Add("return original_module");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string EscapeLuaString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string BuildDeploymentManifest(
        string profileId,
        IReadOnlyList<string> deployedScripts,
        string bootstrapPath,
        string activationStrategy,
        IReadOnlyList<ActivationScriptDeployment> activationScripts,
        object commandTransport,
        IReadOnlyList<object> hooks)
    {
        var manifest = new
        {
            profileId,
            generatedAtUtc = DateTimeOffset.UtcNow,
            bootstrapScript = bootstrapPath,
            activationStrategy,
            activationScripts,
            commandTransport,
            deployedScripts,
            hooks
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private async Task<string> EnsureDeploymentRootAsync(string profileId, CancellationToken cancellationToken)
    {
        if (!await VerifyAsync(profileId, cancellationToken))
        {
            return await DeployAsync(profileId, cancellationToken);
        }

        return ResolveRuntimeDeploymentRoot(profileId);
    }

    private string GetSourceDeploymentRoot(string profileId)
    {
        return Path.Combine(_options.InstallRoot, profileId);
    }

    private string ResolveRuntimeDeploymentRoot(string profileId)
    {
        var gameRoot = ResolveGameRoot();
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            return GetSourceDeploymentRoot(profileId);
        }

        return Path.Combine(gameRoot, "corruption", "Mods", LaunchMirrorRootName, profileId);
    }

    private string? ResolveGameRoot()
    {
        foreach (var candidate in _options.GameRootCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            {
                continue;
            }

            var resolved = Path.GetFullPath(candidate.Trim());
            if (Directory.Exists(Path.Combine(resolved, "corruption")))
            {
                return resolved;
            }
        }

        return null;
    }

    private static void MirrorDeployment(string sourceRoot, string runtimeRoot)
    {
        if (Directory.Exists(runtimeRoot))
        {
            Directory.Delete(runtimeRoot, recursive: true);
        }

        CopyDirectory(sourceRoot, runtimeRoot);
    }

    private string? ResolveTransportArtifactPath(string profileId, string operationToken, string artifactRoot)
    {
        foreach (var root in EnumerateTransportRoots(profileId))
        {
            var candidate = Path.Combine(
                root,
                artifactRoot.Replace('/', Path.DirectorySeparatorChar),
                $"{operationToken}.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateTransportRoots(string profileId)
    {
        var runtimeRoot = ResolveRuntimeDeploymentRoot(profileId);
        if (!string.IsNullOrWhiteSpace(runtimeRoot))
        {
            yield return runtimeRoot;
        }

        var sourceRoot = GetSourceDeploymentRoot(profileId);
        if (!sourceRoot.Equals(runtimeRoot, StringComparison.OrdinalIgnoreCase))
        {
            yield return sourceRoot;
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void EnsureTransportDirectories(string targetRoot)
    {
        Directory.CreateDirectory(GetCommandTransportRoot(targetRoot));
        Directory.CreateDirectory(GetPendingCommandRoot(targetRoot));
        Directory.CreateDirectory(GetClaimedCommandRoot(targetRoot));
        Directory.CreateDirectory(GetReceiptRoot(targetRoot));
    }

    private static void ResetTransportState(string targetRoot)
    {
        var transportRoot = GetCommandTransportRoot(targetRoot);
        if (Directory.Exists(transportRoot))
        {
            Directory.Delete(transportRoot, recursive: true);
        }

        EnsureTransportDirectories(targetRoot);
    }

    private static bool TransportDirectoriesExist(string targetRoot)
    {
        return Directory.Exists(GetCommandTransportRoot(targetRoot)) &&
               Directory.Exists(GetPendingCommandRoot(targetRoot)) &&
               Directory.Exists(GetClaimedCommandRoot(targetRoot)) &&
               Directory.Exists(GetReceiptRoot(targetRoot));
    }

    private static string GetCommandTransportRoot(string targetRoot)
    {
        return Path.Combine(targetRoot, RuntimeTransportRoot.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetPendingCommandRoot(string targetRoot)
    {
        return Path.Combine(targetRoot, PendingCommandRoot.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetDispatchCommandPath(string targetRoot)
    {
        return Path.Combine(targetRoot, DispatchCommandRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetClaimedCommandRoot(string targetRoot)
    {
        return Path.Combine(targetRoot, ClaimedCommandRoot.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetReceiptRoot(string targetRoot)
    {
        return Path.Combine(targetRoot, ReceiptRoot.Replace('/', Path.DirectorySeparatorChar));
    }

    private static object BuildCommandTransportManifest()
    {
        return new
        {
            model = CommandTransportModel,
            schemaVersion = CommandTransportSchemaVersion,
            root = RuntimeTransportRoot,
            pendingDirectory = PendingCommandRoot,
            dispatchCommandPath = DispatchCommandRelativePath,
            claimedDirectory = ClaimedCommandRoot,
            receiptDirectory = ReceiptRoot,
            commandFilePattern = "*.json",
            receiptFilePattern = "*.json",
            dispatchFileFormat = "lua_table",
            executionMode = "bootstrap_dispatch_ready"
        };
    }

    private sealed record ActivationScriptDeployment(
        string Script,
        string DeployedScript,
        string OriginalCopy,
        string OriginalSourcePath,
        string BootstrapRequirePath,
        string OriginalRequirePath);
}
