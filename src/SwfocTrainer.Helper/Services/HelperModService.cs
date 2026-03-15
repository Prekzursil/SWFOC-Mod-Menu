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
            deployedScripts.Add(Path.GetRelativePath(targetRoot, destination).Replace('\\', '/'));
        }

        var bootstrapPath = Path.Combine(GetLibraryRoot(targetRoot), BootstrapScriptName);
        File.WriteAllText(bootstrapPath, BuildBootstrapScript(profile.Id, profile.HelperModHooks));
        File.WriteAllText(Path.Combine(targetRoot, DeploymentManifestFileName), BuildDeploymentManifest(profile.Id, deployedScripts, bootstrapPath));

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

    private static string BuildBootstrapScript(string profileId, IReadOnlyList<SwfocTrainer.Core.Models.HelperHookSpec> hooks)
    {
        var hookLines = hooks
            .Select(static hook => $"-- hook: {hook.Id} => {hook.Script}")
            .ToArray();

        return string.Join(
            Environment.NewLine,
            new[]
            {
                "-- Auto-generated by SWFOC Trainer.",
                $"SWFOC_TRAINER_HELPER_PROFILE = \"{profileId}\"",
                $"SWFOC_TRAINER_HELPER_HOOK_COUNT = {hooks.Count}",
                "function SwfocTrainer_Helper_Bootstrap_Describe()",
                "    return SWFOC_TRAINER_HELPER_PROFILE",
                "end"
            }.Concat(hookLines)) + Environment.NewLine;
    }

    private static string BuildDeploymentManifest(string profileId, IReadOnlyList<string> deployedScripts, string bootstrapPath)
    {
        var manifest = new
        {
            profileId,
            generatedAtUtc = DateTimeOffset.UtcNow,
            bootstrapScript = bootstrapPath,
            deployedScripts
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
