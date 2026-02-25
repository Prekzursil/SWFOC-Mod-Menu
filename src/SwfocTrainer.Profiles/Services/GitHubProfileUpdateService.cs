#pragma warning disable S4136
using System.IO.Compression;
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
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: "Remote manifest URL is not configured.",
                ReasonCode: "remote_manifest_not_configured");
        }

        ProfileManifest? remote;
        try
        {
            remote = await _httpClient.GetFromJsonAsync<ProfileManifest>(_options.RemoteManifestUrl, _jsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"Failed to fetch remote manifest: {ex.Message}",
                ReasonCode: "manifest_fetch_failed");
        }

        if (remote is null)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: "Remote manifest payload is empty.",
                ReasonCode: "manifest_empty");
        }

        var entry = remote.Profiles.FirstOrDefault(x => string.Equals(x.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"Profile '{profileId}' is not present in remote manifest.",
                ReasonCode: "profile_missing_in_manifest");
        }

        var zipPath = Path.Combine(_options.DownloadCachePath, $"{profileId}-{entry.Version}.zip");
        try
        {
            await using (var stream = await _httpClient.GetStreamAsync(entry.DownloadUrl, cancellationToken))
            await using (var file = File.Create(zipPath))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"Failed to download package: {ex.Message}",
                ReasonCode: "download_failed");
        }

        var sha = ComputeSha256(zipPath);
        if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"SHA mismatch for {profileId}. Expected {entry.Sha256}, got {sha}",
                ReasonCode: "sha_mismatch");
        }

        var profilesDir = Path.Combine(_options.ProfilesRootPath, "profiles");
        Directory.CreateDirectory(profilesDir);

        var extractDir = Path.Combine(_options.DownloadCachePath, $"extract-{profileId}-{entry.Version}");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        try
        {
            ExtractToDirectorySafely(zipPath, extractDir);
        }
        catch (Exception ex)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"Failed to extract package: {ex.Message}",
                ReasonCode: "extract_failed");
        }

        var targetProfileJson = Directory.GetFiles(extractDir, $"{profileId}.json", SearchOption.AllDirectories).FirstOrDefault();
        if (targetProfileJson is null)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"Downloaded package does not contain '{profileId}.json'.",
                ReasonCode: "profile_json_missing");
        }

        var profileJson = await File.ReadAllTextAsync(targetProfileJson, cancellationToken);
        var parsedProfile = JsonProfileSerializer.Deserialize<TrainerProfile>(profileJson);
        if (parsedProfile is null)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: "Downloaded profile JSON failed to deserialize.",
                ReasonCode: "profile_deserialize_failed");
        }

        try
        {
            ProfileValidator.Validate(parsedProfile);
        }
        catch (Exception ex)
        {
            return new ProfileInstallResult(
                Succeeded: false,
                ProfileId: profileId,
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: $"Downloaded profile failed validation: {ex.Message}",
                ReasonCode: "profile_validation_failed");
        }

        var destination = Path.Combine(profilesDir, $"{profileId}.json");
        var backup = $"{destination}.bak.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
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

        var receiptPath = await WriteInstallReceiptAsync(
            profileId,
            destination,
            backupPath,
            entry.Version,
            entry.Sha256,
            cancellationToken);

        return new ProfileInstallResult(
            Succeeded: true,
            ProfileId: profileId,
            InstalledPath: destination,
            BackupPath: backupPath,
            ReceiptPath: receiptPath,
            Message: $"Installed profile update '{profileId}' ({entry.Version}).",
            ReasonCode: null);
    }

    private static void ExtractToDirectorySafely(string zipPath, string extractDir)
    {
        var extractionRoot = Path.GetFullPath(extractDir);
        Directory.CreateDirectory(extractionRoot);
        var extractionRootPrefix = extractionRoot.EndsWith(Path.DirectorySeparatorChar)
            ? extractionRoot
            : extractionRoot + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            var normalizedEntryPath = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedEntryPath))
            {
                continue;
            }

            if (IsDriveQualifiedPath(normalizedEntryPath))
            {
                throw new InvalidDataException($"Archive entry uses drive-qualified path: {entry.FullName}");
            }

            if (Path.IsPathRooted(normalizedEntryPath) || normalizedEntryPath.StartsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Archive entry uses rooted path: {entry.FullName}");
            }

            var relativePath = normalizedEntryPath.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(extractionRoot, relativePath));
            if (!destinationPath.StartsWith(extractionRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry escapes extraction root: {entry.FullName}");
            }

            if (normalizedEntryPath.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var entryStream = entry.Open();
            using var outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(outputStream);
        }
    }

    private static bool IsDriveQualifiedPath(string path)
        => path.Length >= 2 &&
           char.IsLetter(path[0]) &&
           path[1] == ':';

    public async Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken)
    {
        var profilesDir = Path.Combine(_options.ProfilesRootPath, "profiles");
        Directory.CreateDirectory(profilesDir);
        var destination = Path.Combine(profilesDir, $"{profileId}.json");
        var backupPattern = $"{profileId}.json.bak.*";

        var backup = Directory.GetFiles(profilesDir, backupPattern, SearchOption.TopDirectoryOnly)
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
        var receiptsRoot = Path.Combine(_options.DownloadCachePath, "install-receipts");
        Directory.CreateDirectory(receiptsRoot);
        var receiptPath = Path.Combine(receiptsRoot, $"install-{profileId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");

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
        var receiptsRoot = Path.Combine(_options.DownloadCachePath, "install-receipts");
        Directory.CreateDirectory(receiptsRoot);
        var receiptPath = Path.Combine(receiptsRoot, $"rollback-{profileId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");

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
