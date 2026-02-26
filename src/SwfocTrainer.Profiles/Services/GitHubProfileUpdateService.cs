#pragma warning disable S4136
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Validation;

namespace SwfocTrainer.Profiles.Services;

public sealed class GitHubProfileUpdateService : IProfileUpdateService
{
    private const string ProfilesDirectoryName = "profiles";
    private const string ReceiptsDirectoryName = "install-receipts";
    private const string ReceiptTimestampFormat = "yyyyMMddHHmmss";

    private readonly HttpClient _httpClient;
    private readonly ProfileRepositoryOptions _options;
    private readonly IProfileRepository _repository;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubProfileUpdateService(HttpClient httpClient, ProfileRepositoryOptions options, IProfileRepository repository)
    {
        _httpClient = httpClient;
        _options = options;
        _repository = repository;
        Directory.CreateDirectory(_options.DownloadCachePath);
    }

    public async Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.RemoteManifestUrl))
        {
            return Array.Empty<string>();
        }

        var remote = await _httpClient.GetFromJsonAsync<ProfileManifest>(_options.RemoteManifestUrl, _jsonOptions, cancellationToken);
        if (remote is null)
        {
            return Array.Empty<string>();
        }

        var local = await _repository.LoadManifestAsync(cancellationToken);
        var localVersions = local.Profiles.ToDictionary(x => x.Id, x => x.Version, StringComparer.OrdinalIgnoreCase);

        var updates = new List<string>();
        foreach (var entry in remote.Profiles)
        {
            if (!localVersions.TryGetValue(entry.Id, out var localVersion) || !string.Equals(localVersion, entry.Version, StringComparison.OrdinalIgnoreCase))
            {
                updates.Add(entry.Id);
            }
        }

        return updates;
    }

    public async Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        var result = await InstallProfileTransactionalAsync(profileId, cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.InstalledPath;
    }

    public async Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.RemoteManifestUrl))
        {
            return BuildInstallFailure(profileId, "Remote manifest URL is not configured.", "remote_manifest_not_configured");
        }

        var manifestEntryOutcome = await TryResolveManifestEntryAsync(profileId, cancellationToken);
        if (manifestEntryOutcome.Failure is not null)
        {
            return manifestEntryOutcome.Failure;
        }

        var entry = manifestEntryOutcome.Entry!;
        var preparedInstall = await TryPrepareProfileInstallAsync(profileId, entry, cancellationToken);
        if (preparedInstall.Failure is not null)
        {
            return preparedInstall.Failure;
        }

        var (destination, backupPath) = InstallProfileFile(profileId, preparedInstall.TargetProfileJson!, preparedInstall.ProfilesDirectory!);
        var receiptPath = await WriteInstallReceiptAsync(
            profileId,
            destination,
            backupPath,
            entry.Version,
            entry.Sha256,
            cancellationToken);

        return BuildInstallSuccess(profileId, destination, backupPath, receiptPath, entry.Version);
    }

    public async Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken)
    {
        var profilesDir = ResolveProfilesDirectory();
        var destination = Path.Combine(profilesDir, $"{profileId}.json");
        var backupPattern = $"{profileId}.json.bak.*";

        var backup = Directory.GetFiles(profilesDir, backupPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (backup is null)
        {
            return new ProfileRollbackResult(
                Restored: false,
                ProfileId: profileId,
                RestoredPath: destination,
                BackupPath: null,
                Message: $"No rollback backup found for '{profileId}'.",
                ReasonCode: "backup_not_found");
        }

        try
        {
            File.Copy(backup, destination, overwrite: true);
            await WriteRollbackReceiptAsync(profileId, destination, backup, cancellationToken);
            return new ProfileRollbackResult(
                Restored: true,
                ProfileId: profileId,
                RestoredPath: destination,
                BackupPath: backup,
                Message: $"Rollback restored '{profileId}' from backup.",
                ReasonCode: null);
        }
        catch (Exception ex)
        {
            return new ProfileRollbackResult(
                Restored: false,
                ProfileId: profileId,
                RestoredPath: destination,
                BackupPath: backup,
                Message: $"Rollback failed: {ex.Message}",
                ReasonCode: "rollback_copy_failed");
        }
    }

    public Task<IReadOnlyList<string>> CheckForUpdatesAsync()
    {
        return CheckForUpdatesAsync(CancellationToken.None);
    }

    public Task<string> InstallProfileAsync(string profileId)
    {
        return InstallProfileAsync(profileId, CancellationToken.None);
    }

    public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId)
    {
        return InstallProfileTransactionalAsync(profileId, CancellationToken.None);
    }

    public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId)
    {
        return RollbackLastInstallAsync(profileId, CancellationToken.None);
    }

    private async Task<(ProfileManifest? Manifest, ProfileInstallResult? Failure)> TryLoadRemoteManifestAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ProfileManifest? remote;
        try
        {
            remote = await _httpClient.GetFromJsonAsync<ProfileManifest>(_options.RemoteManifestUrl, _jsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            return (
                null,
                BuildInstallFailure(
                    profileId,
                    $"Failed to fetch remote manifest: {ex.Message}",
                    "manifest_fetch_failed"));
        }

        if (remote is null)
        {
            return (null, BuildInstallFailure(profileId, "Remote manifest payload is empty.", "manifest_empty"));
        }

        return (remote, null);
    }

    private async Task<(ProfileManifestEntry? Entry, ProfileInstallResult? Failure)> TryResolveManifestEntryAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var remoteManifestOutcome = await TryLoadRemoteManifestAsync(profileId, cancellationToken);
        if (remoteManifestOutcome.Failure is not null)
        {
            return (null, remoteManifestOutcome.Failure);
        }

        var entry = remoteManifestOutcome.Manifest!.Profiles
            .FirstOrDefault(x => string.Equals(x.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return (
                null,
                BuildInstallFailure(
                    profileId,
                    $"Profile '{profileId}' is not present in remote manifest.",
                    "profile_missing_in_manifest"));
        }

        return (entry, null);
    }

    private async Task<(string? ProfilesDirectory, string? TargetProfileJson, ProfileInstallResult? Failure)> TryPrepareProfileInstallAsync(
        string profileId,
        ProfileManifestEntry entry,
        CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(_options.DownloadCachePath, $"{profileId}-{entry.Version}.zip");
        var downloadFailure = await TryDownloadPackageAsync(profileId, entry.DownloadUrl, zipPath, cancellationToken);
        if (downloadFailure is not null)
        {
            return (null, null, downloadFailure);
        }

        var integrityFailure = ValidateDownloadedPackageIntegrity(profileId, entry, zipPath);
        if (integrityFailure is not null)
        {
            return (null, null, integrityFailure);
        }

        var extractDir = PrepareExtractDirectory(profileId, entry.Version);
        var extractFailure = TryExtractPackage(profileId, zipPath, extractDir);
        if (extractFailure is not null)
        {
            return (null, null, extractFailure);
        }

        var targetProfileJson = FindExtractedProfileJson(extractDir, profileId);
        if (targetProfileJson is null)
        {
            return (
                null,
                null,
                BuildInstallFailure(
                    profileId,
                    $"Downloaded package does not contain '{profileId}.json'.",
                    "profile_json_missing"));
        }

        var profileValidationFailure = await ValidateDownloadedProfileAsync(profileId, targetProfileJson, cancellationToken);
        if (profileValidationFailure is not null)
        {
            return (null, null, profileValidationFailure);
        }

        return (ResolveProfilesDirectory(), targetProfileJson, null);
    }

    private async Task<ProfileInstallResult?> TryDownloadPackageAsync(
        string profileId,
        string downloadUrl,
        string zipPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using (var stream = await _httpClient.GetStreamAsync(downloadUrl, cancellationToken))
            await using (var file = File.Create(zipPath))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return BuildInstallFailure(profileId, $"Failed to download package: {ex.Message}", "download_failed");
        }

        return null;
    }

    private static ProfileInstallResult? ValidateDownloadedPackageIntegrity(string profileId, ProfileManifestEntry entry, string zipPath)
    {
        var sha = ComputeSha256(zipPath);
        if (string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return BuildInstallFailure(
            profileId,
            $"SHA mismatch for {profileId}. Expected {entry.Sha256}, got {sha}",
            "sha_mismatch");
    }

    private static ProfileInstallResult? TryExtractPackage(
        string profileId,
        string zipPath,
        string extractDir)
    {
        try
        {
            GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(zipPath, extractDir);
        }
        catch (Exception ex)
        {
            return BuildInstallFailure(profileId, $"Failed to extract package: {ex.Message}", "extract_failed");
        }

        return null;
    }

    private static async Task<ProfileInstallResult?> ValidateDownloadedProfileAsync(
        string profileId,
        string targetProfileJson,
        CancellationToken cancellationToken)
    {
        var profileJson = await File.ReadAllTextAsync(targetProfileJson, cancellationToken);
        var parsedProfile = JsonProfileSerializer.Deserialize<TrainerProfile>(profileJson);
        if (parsedProfile is null)
        {
            return BuildInstallFailure(profileId, "Downloaded profile JSON failed to deserialize.", "profile_deserialize_failed");
        }

        try
        {
            ProfileValidator.Validate(parsedProfile);
        }
        catch (Exception ex)
        {
            return BuildInstallFailure(profileId, $"Downloaded profile failed validation: {ex.Message}", "profile_validation_failed");
        }

        return null;
    }

    private static (string Destination, string? BackupPath) InstallProfileFile(string profileId, string targetProfileJson, string profilesDir)
    {
        var destination = Path.Combine(profilesDir, $"{profileId}.json");
        var backup = $"{destination}.bak.{DateTimeOffset.UtcNow.ToString(ReceiptTimestampFormat)}";
        string? backupPath = null;
        if (File.Exists(destination))
        {
            File.Copy(destination, backup, overwrite: true);
            backupPath = backup;
        }

        var tempInstallPath = $"{destination}.tmp";
        File.Copy(targetProfileJson, tempInstallPath, overwrite: true);
        try
        {
            File.Copy(tempInstallPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempInstallPath))
            {
                File.Delete(tempInstallPath);
            }
        }

        return (destination, backupPath);
    }

    private string ResolveProfilesDirectory()
    {
        var profilesDir = Path.Combine(_options.ProfilesRootPath, ProfilesDirectoryName);
        Directory.CreateDirectory(profilesDir);
        return profilesDir;
    }

    private string PrepareExtractDirectory(string profileId, string version)
    {
        var extractDir = Path.Combine(_options.DownloadCachePath, $"extract-{profileId}-{version}");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        return extractDir;
    }

    private static ProfileInstallResult BuildInstallFailure(string profileId, string message, string reasonCode)
    {
        return new ProfileInstallResult(
            Succeeded: false,
            ProfileId: profileId,
            InstalledPath: string.Empty,
            BackupPath: null,
            ReceiptPath: null,
            Message: message,
            ReasonCode: reasonCode);
    }

    private static string? FindExtractedProfileJson(string extractDir, string profileId)
    {
        return Directory.GetFiles(extractDir, $"{profileId}.json", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static ProfileInstallResult BuildInstallSuccess(
        string profileId,
        string destination,
        string? backupPath,
        string receiptPath,
        string version)
    {
        return new ProfileInstallResult(
            Succeeded: true,
            ProfileId: profileId,
            InstalledPath: destination,
            BackupPath: backupPath,
            ReceiptPath: receiptPath,
            Message: $"Installed profile update '{profileId}' ({version}).",
            ReasonCode: null);
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string> WriteInstallReceiptAsync(
        string profileId,
        string installedPath,
        string? backupPath,
        string version,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var receiptsRoot = Path.Combine(_options.DownloadCachePath, ReceiptsDirectoryName);
        Directory.CreateDirectory(receiptsRoot);
        var receiptPath = Path.Combine(receiptsRoot, $"install-{profileId}-{DateTimeOffset.UtcNow.ToString(ReceiptTimestampFormat)}.json");

        var payload = new
        {
            type = "install",
            generatedAtUtc = DateTimeOffset.UtcNow,
            profileId,
            installedPath,
            backupPath,
            version,
            expectedSha256
        };

        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(payload, _jsonOptions), cancellationToken);
        return receiptPath;
    }

    private async Task<string> WriteRollbackReceiptAsync(
        string profileId,
        string restoredPath,
        string backupPath,
        CancellationToken cancellationToken)
    {
        var receiptsRoot = Path.Combine(_options.DownloadCachePath, ReceiptsDirectoryName);
        Directory.CreateDirectory(receiptsRoot);
        var receiptPath = Path.Combine(receiptsRoot, $"rollback-{profileId}-{DateTimeOffset.UtcNow.ToString(ReceiptTimestampFormat)}.json");

        var payload = new
        {
            type = "rollback",
            generatedAtUtc = DateTimeOffset.UtcNow,
            profileId,
            restoredPath,
            backupPath
        };

        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(payload, _jsonOptions), cancellationToken);
        return receiptPath;
    }
}
