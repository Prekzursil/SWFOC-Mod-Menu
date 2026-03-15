#pragma warning disable S4136
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Helper.Config;

namespace SwfocTrainer.Helper.Services;

public sealed class HelperModService : IHelperModService
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
    private const string ClaimedCommandRoot = "SwfocTrainer/Runtime/commands/claimed";
    private const string ReceiptRoot = "SwfocTrainer/Runtime/receipts";

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
        var targetRoot = Path.Combine(_options.InstallRoot, profileId);
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(GetLibraryRoot(targetRoot));
        EnsureTransportDirectories(targetRoot);

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

            var destination = GetDeployedHookScriptPath(targetRoot, script);
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(sourcePath, destination, overwrite: true);
            var relativeDeployedScript = Path.GetRelativePath(targetRoot, destination).Replace('\\', '/');
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

        var bootstrapPath = Path.Combine(GetLibraryRoot(targetRoot), BootstrapScriptName);
        File.WriteAllText(bootstrapPath, BuildBootstrapScript(profile.Id, profile.HelperModHooks));

        var activationStrategy = ResolveActivationStrategy(profile);
        var activationScripts = await DeployActivationScriptsAsync(targetRoot, profile, activationStrategy, cancellationToken);

        File.WriteAllText(
            Path.Combine(targetRoot, DeploymentManifestFileName),
            BuildDeploymentManifest(
                profile.Id,
                deployedScripts,
                Path.GetRelativePath(targetRoot, bootstrapPath).Replace('\\', '/'),
                activationStrategy,
                activationScripts,
                BuildCommandTransportManifest(),
                deployedHookManifests));

        _logger.LogInformation(
            "Deployed helper hooks for {ProfileId} into {TargetRoot} (activationStrategy={ActivationStrategy}, activationScripts={ActivationCount})",
            profileId,
            targetRoot,
            activationStrategy,
            activationScripts.Count);

        return targetRoot;
    }

    public async Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        var targetRoot = Path.Combine(_options.InstallRoot, profileId);
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

        var activationStrategy = ResolveActivationStrategy(profile);
        if (string.IsNullOrWhiteSpace(activationStrategy))
        {
            return true;
        }

        foreach (var activationScript in ResolveAutoloadScripts(profile))
        {
            if (!File.Exists(GetActivationWrapperPath(targetRoot, activationScript)) ||
                !File.Exists(GetOriginalCopyPath(targetRoot, activationScript)))
            {
                return false;
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

    private static string BuildBootstrapScript(string profileId, IReadOnlyList<HelperHookSpec> hooks)
    {
        var lines = new List<string>
        {
            "-- Auto-generated by SWFOC Trainer.",
            $"SWFOC_TRAINER_HELPER_PROFILE = \"{EscapeLuaString(profileId)}\"",
            $"SWFOC_TRAINER_HELPER_HOOK_COUNT = {hooks.Count}",
            $"SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT = \"{EscapeLuaString(CommandTransportModel)}\"",
            $"SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT_SCHEMA = \"{EscapeLuaString(CommandTransportSchemaVersion)}\"",
            $"SWFOC_TRAINER_HELPER_COMMAND_ROOT = \"{EscapeLuaString(RuntimeTransportRoot)}\"",
            $"SWFOC_TRAINER_HELPER_COMMAND_PENDING = \"{EscapeLuaString(PendingCommandRoot)}\"",
            $"SWFOC_TRAINER_HELPER_COMMAND_CLAIMED = \"{EscapeLuaString(ClaimedCommandRoot)}\"",
            $"SWFOC_TRAINER_HELPER_RECEIPT_ROOT = \"{EscapeLuaString(ReceiptRoot)}\"",
            "SWFOC_TRAINER_HELPER_HOOKS = {"
        };

        foreach (var hook in hooks)
        {
            lines.Add("    {");
            lines.Add($"        id = \"{EscapeLuaString(hook.Id)}\",");
            lines.Add($"        script = \"{EscapeLuaString(hook.Script.Replace('\\', '/'))}\",");
            lines.Add($"        requirePath = \"{EscapeLuaString(NormalizeHookLuaRequirePath(hook.Script))}\",");
            lines.Add($"        entryPoint = \"{EscapeLuaString(hook.EntryPoint ?? string.Empty)}\",");
            lines.Add($"        version = \"{EscapeLuaString(hook.Version)}\"");
            lines.Add("    },");
        }

        lines.Add("}");
        lines.Add(string.Empty);
        lines.Add("local function SwfocTrainer_Helper_Bootstrap_Output(message)");
        lines.Add("    if message == nil or message == \"\" then");
        lines.Add("        return");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    if _OuputDebug then");
        lines.Add("        pcall(function()");
        lines.Add("            _OuputDebug(message)");
        lines.Add("        end)");
        lines.Add("        return");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    if _OutputDebug then");
        lines.Add("        pcall(function()");
        lines.Add("            _OutputDebug(message)");
        lines.Add("        end)");
        lines.Add("    end");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("function SwfocTrainer_Helper_Bootstrap_Describe()");
        lines.Add("    return {");
        lines.Add("        profile = SWFOC_TRAINER_HELPER_PROFILE,");
        lines.Add("        hookCount = SWFOC_TRAINER_HELPER_HOOK_COUNT,");
        lines.Add("        transport = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT,");
        lines.Add("        schemaVersion = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT_SCHEMA");
        lines.Add("    }");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("function SwfocTrainer_Helper_Bootstrap_DescribeTransport()");
        lines.Add("    return {");
        lines.Add("        model = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT,");
        lines.Add("        schemaVersion = SWFOC_TRAINER_HELPER_COMMAND_TRANSPORT_SCHEMA,");
        lines.Add("        root = SWFOC_TRAINER_HELPER_COMMAND_ROOT,");
        lines.Add("        pending = SWFOC_TRAINER_HELPER_COMMAND_PENDING,");
        lines.Add("        claimed = SWFOC_TRAINER_HELPER_COMMAND_CLAIMED,");
        lines.Add("        receipts = SWFOC_TRAINER_HELPER_RECEIPT_ROOT");
        lines.Add("    }");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("local function SwfocTrainer_Helper_Bootstrap_Resolve_Command_EntryPoint(command)");
        lines.Add("    if type(command) ~= \"table\" then");
        lines.Add("        return nil");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    local candidate = command[\"helperEntryPoint\"]");
        lines.Add("    if candidate == nil or candidate == \"\" then");
        lines.Add("        candidate = command[\"entryPoint\"]");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    return candidate");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("local function SwfocTrainer_Helper_Bootstrap_Resolve_Command_Args(command)");
        lines.Add("    if type(command) ~= \"table\" then");
        lines.Add("        return {}");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    local args = command[\"args\"]");
        lines.Add("    if type(args) == \"table\" then");
        lines.Add("        return args");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    return {}");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("local function SwfocTrainer_Helper_Bootstrap_Invoke_EntryPoint(entryPoint, args)");
        lines.Add("    if entryPoint == nil or entryPoint == \"\" then");
        lines.Add("        return false, \"missing_entry_point\"");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    local fn = _G[entryPoint]");
        lines.Add("    if type(fn) ~= \"function\" then");
        lines.Add("        return false, \"missing_runtime_function\"");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    local ok, result = pcall(function()");
        lines.Add("        return fn(table.unpack(args))");
        lines.Add("    end)");
        lines.Add("    if not ok then");
        lines.Add("        return false, tostring(result)");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    if result == nil then");
        lines.Add("        return true, \"invoked\"");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    return result and true or false, tostring(result)");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("function SwfocTrainer_Helper_Bootstrap_Execute_Command(command)");
        lines.Add("    local entryPoint = SwfocTrainer_Helper_Bootstrap_Resolve_Command_EntryPoint(command)");
        lines.Add("    local args = SwfocTrainer_Helper_Bootstrap_Resolve_Command_Args(command)");
        lines.Add("    local operationToken = \"\"");
        lines.Add("    if type(command) == \"table\" then");
        lines.Add("        operationToken = tostring(command[\"operationToken\"] or command[\"operation_token\"] or \"\")");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    local ok, detail = SwfocTrainer_Helper_Bootstrap_Invoke_EntryPoint(entryPoint, args)");
        lines.Add("    if ok then");
        lines.Add("        SwfocTrainer_Helper_Bootstrap_Output(\"SWFOC_TRAINER_HELPER_COMMAND_ACCEPTED profile=\" .. SWFOC_TRAINER_HELPER_PROFILE .. \" entry=\" .. tostring(entryPoint) .. \" token=\" .. operationToken)");
        lines.Add("        return true");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    SwfocTrainer_Helper_Bootstrap_Output(\"SWFOC_TRAINER_HELPER_COMMAND_FAILED profile=\" .. SWFOC_TRAINER_HELPER_PROFILE .. \" entry=\" .. tostring(entryPoint) .. \" token=\" .. operationToken .. \" error=\" .. tostring(detail))");
        lines.Add("    return false");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("function SwfocTrainer_Helper_Bootstrap_LoadAll()");
        lines.Add("    local loaded = 0");
        lines.Add("    for _, hook in ipairs(SWFOC_TRAINER_HELPER_HOOKS) do");
        lines.Add("        local ok, module_or_error = pcall(require, hook.requirePath)");
        lines.Add("        if ok then");
        lines.Add("            loaded = loaded + 1");
        lines.Add("            SwfocTrainer_Helper_Bootstrap_Output(\"SWFOC_TRAINER_HELPER_BOOTSTRAP_LOADED profile=\" .. SWFOC_TRAINER_HELPER_PROFILE .. \" hook=\" .. hook.id .. \" require=\" .. hook.requirePath)");
        lines.Add("        else");
        lines.Add("            SwfocTrainer_Helper_Bootstrap_Output(\"SWFOC_TRAINER_HELPER_BOOTSTRAP_FAILED profile=\" .. SWFOC_TRAINER_HELPER_PROFILE .. \" hook=\" .. hook.id .. \" require=\" .. hook.requirePath .. \" error=\" .. tostring(module_or_error))");
        lines.Add("        end");
        lines.Add("    end");
        lines.Add(string.Empty);
        lines.Add("    return loaded");
        lines.Add("end");
        lines.Add(string.Empty);
        lines.Add("SwfocTrainer_Helper_Bootstrap_LoadAll()");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
            "local bootstrap_ok, bootstrap_error = pcall(function()",
            $"    return require(\"{EscapeLuaString(BootstrapRequirePath)}\")",
            "end)",
            "if bootstrap_ok then",
            $"    SwfocTrainer_Helper_Autoload_Output(\"SWFOC_TRAINER_HELPER_AUTOLOAD_READY profile={EscapeLuaString(profileId)} strategy={EscapeLuaString(activationStrategy)} script={EscapeLuaString(normalizedScriptPath)}\")",
            "else",
            $"    SwfocTrainer_Helper_Autoload_Output(\"SWFOC_TRAINER_HELPER_AUTOLOAD_FAILED profile={EscapeLuaString(profileId)} strategy={EscapeLuaString(activationStrategy)} script={EscapeLuaString(normalizedScriptPath)} error=\" .. tostring(bootstrap_error))",
            "end",
            string.Empty,
            $"return require(\"{EscapeLuaString(originalRequirePath)}\")"
        };

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

    private static void EnsureTransportDirectories(string targetRoot)
    {
        Directory.CreateDirectory(GetCommandTransportRoot(targetRoot));
        Directory.CreateDirectory(GetPendingCommandRoot(targetRoot));
        Directory.CreateDirectory(GetClaimedCommandRoot(targetRoot));
        Directory.CreateDirectory(GetReceiptRoot(targetRoot));
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
            claimedDirectory = ClaimedCommandRoot,
            receiptDirectory = ReceiptRoot,
            commandFilePattern = "*.json",
            receiptFilePattern = "*.json",
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
