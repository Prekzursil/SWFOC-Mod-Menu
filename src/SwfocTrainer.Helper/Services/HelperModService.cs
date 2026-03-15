#pragma warning disable S4136
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Helper.Config;

namespace SwfocTrainer.Helper.Services;

public sealed class HelperModService : IHelperModService
{
    private const string DeploymentManifestFileName = "helper-deployment.json";
    private const string BootstrapScriptName = "SwfocTrainer_HelperBootstrap.lua";

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

            var destination = GetDeployedScriptPath(targetRoot, script);
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
                requirePath = NormalizeLuaRequirePath(hook.Script),
                entryPoint = hook.EntryPoint ?? string.Empty,
                version = hook.Version,
                sha256 = ComputeSha256(destination)
            });
        }

        var bootstrapPath = Path.Combine(GetLibraryRoot(targetRoot), BootstrapScriptName);
        File.WriteAllText(bootstrapPath, BuildBootstrapScript(profile.Id, profile.HelperModHooks));
        File.WriteAllText(
            Path.Combine(targetRoot, DeploymentManifestFileName),
            BuildDeploymentManifest(
                profile.Id,
                deployedScripts,
                Path.GetRelativePath(targetRoot, bootstrapPath).Replace('\\', '/'),
                deployedHookManifests));

        _logger.LogInformation("Deployed helper hooks for {ProfileId} into {TargetRoot}", profileId, targetRoot);
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

        foreach (var hook in profile.HelperModHooks)
        {
            var path = GetDeployedScriptPath(targetRoot, hook.Script);
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

        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public Task<string> DeployAsync(string profileId)
    {
        return DeployAsync(profileId, CancellationToken.None);
    }

    public Task<bool> VerifyAsync(string profileId)
    {
        return VerifyAsync(profileId, CancellationToken.None);
    }

    private static string GetLibraryRoot(string targetRoot)
    {
        return Path.Combine(targetRoot, "Data", "Scripts", "Library");
    }

    private static string GetDeployedScriptPath(string targetRoot, string script)
    {
        return Path.Combine(GetLibraryRoot(targetRoot), NormalizeScriptRelativePath(script));
    }

    private static string NormalizeScriptRelativePath(string script)
    {
        var normalized = script
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var scriptPrefix = $"scripts{Path.DirectorySeparatorChar}";
        if (normalized.StartsWith(scriptPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[scriptPrefix.Length..];
        }

        return normalized;
    }

    private static string NormalizeLuaRequirePath(string script)
    {
        var relativePath = NormalizeScriptRelativePath(script)
            .Replace(Path.DirectorySeparatorChar, '.');

        if (relativePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath[..^4];
        }

        return relativePath.Trim('.');
    }

    private static string BuildBootstrapScript(string profileId, IReadOnlyList<SwfocTrainer.Core.Models.HelperHookSpec> hooks)
    {
        var lines = new List<string>
        {
            "-- Auto-generated by SWFOC Trainer.",
            $"SWFOC_TRAINER_HELPER_PROFILE = \"{EscapeLuaString(profileId)}\"",
            $"SWFOC_TRAINER_HELPER_HOOK_COUNT = {hooks.Count}",
            "SWFOC_TRAINER_HELPER_HOOKS = {"
        };

        foreach (var hook in hooks)
        {
            lines.Add("    {");
            lines.Add($"        id = \"{EscapeLuaString(hook.Id)}\",");
            lines.Add($"        script = \"{EscapeLuaString(hook.Script.Replace('\\', '/'))}\",");
            lines.Add($"        requirePath = \"{EscapeLuaString(NormalizeLuaRequirePath(hook.Script))}\",");
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
        lines.Add("    return SWFOC_TRAINER_HELPER_PROFILE");
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
        IReadOnlyList<object> hooks)
    {
        var manifest = new
        {
            profileId,
            generatedAtUtc = DateTimeOffset.UtcNow,
            bootstrapScript = bootstrapPath,
            deployedScripts,
            hooks
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
