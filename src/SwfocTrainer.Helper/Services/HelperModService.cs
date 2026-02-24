#pragma warning disable S4136
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Helper.Config;

namespace SwfocTrainer.Helper.Services;

public sealed class HelperModService : IHelperModService
{
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

        foreach (var script in profile.HelperModHooks.Select(hook => hook.Script))
        {
            var sourcePath = Path.Combine(_options.SourceRoot, script);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Helper hook source not found: {sourcePath}");
            }

            var destination = Path.Combine(targetRoot, script);
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(sourcePath, destination, overwrite: true);
        }

        _logger.LogInformation("Deployed helper hooks for {ProfileId} into {TargetRoot}", profileId, targetRoot);
        return targetRoot;
    }

    public async Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        var targetRoot = Path.Combine(_options.InstallRoot, profileId);

        foreach (var hook in profile.HelperModHooks)
        {
            var path = Path.Combine(targetRoot, hook.Script);
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
}
