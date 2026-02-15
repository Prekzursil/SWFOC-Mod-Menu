using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;

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

    public async Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
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

    public async Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.RemoteManifestUrl))
        {
            throw new InvalidOperationException("Remote manifest URL is not configured.");
        }

        var remote = await _httpClient.GetFromJsonAsync<ProfileManifest>(_options.RemoteManifestUrl, _jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Failed to fetch remote manifest.");

        var entry = remote.Profiles.FirstOrDefault(x => string.Equals(x.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException($"Profile '{profileId}' is not present in remote manifest.");
        }

        var zipPath = Path.Combine(_options.DownloadCachePath, $"{profileId}-{entry.Version}.zip");
        await using (var stream = await _httpClient.GetStreamAsync(entry.DownloadUrl, cancellationToken))
        await using (var file = File.Create(zipPath))
        {
            await stream.CopyToAsync(file, cancellationToken);
        }

        var sha = ComputeSha256(zipPath);
        if (!string.Equals(sha, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA mismatch for {profileId}. Expected {entry.Sha256}, got {sha}");
        }

        var profilesDir = Path.Combine(_options.ProfilesRootPath, "profiles");
        Directory.CreateDirectory(profilesDir);

        var extractDir = Path.Combine(_options.DownloadCachePath, $"extract-{profileId}-{entry.Version}");
        if (Directory.Exists(extractDir))
        {
            Directory.Delete(extractDir, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var targetProfileJson = Directory.GetFiles(extractDir, $"{profileId}.json", SearchOption.AllDirectories).FirstOrDefault();
        if (targetProfileJson is null)
        {
            throw new InvalidDataException($"Downloaded package does not contain '{profileId}.json'");
        }

        var destination = Path.Combine(profilesDir, $"{profileId}.json");
        var backup = destination + ".bak";
        if (File.Exists(destination))
        {
            File.Copy(destination, backup, overwrite: true);
        }

        File.Copy(targetProfileJson, destination, overwrite: true);
        return destination;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
