using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ProfileUpdateServiceCoverageSweepTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnChangedAndMissingProfiles()
    {
        using var tempRoot = new TempRoot();
        var profileId = "base_swfoc";
        var service = CreateService(
            tempRoot,
            CreateManifestHandler(
                new ProfileManifest(
                    "1.0.0",
                    DateTimeOffset.UtcNow,
                    new[]
                    {
                        new ProfileManifestEntry("alpha", "1.0.0", "sha-a", "https://example.invalid/alpha.zip", "1.0.0"),
                        new ProfileManifestEntry("beta", "2.0.0", "sha-b", "https://example.invalid/beta.zip", "1.0.0"),
                        new ProfileManifestEntry(profileId, "9.9.9", "sha-c", $"https://example.invalid/{profileId}.zip", "1.0.0")
                    })),
            new StubProfileRepository(
                manifest: new ProfileManifest(
                    "1.0.0",
                    DateTimeOffset.UtcNow,
                    new[]
                    {
                        new ProfileManifestEntry("alpha", "1.0.0", "sha-a", "unused", "1.0.0"),
                        new ProfileManifestEntry(profileId, "1.0.0", "sha-c", "unused", "1.0.0")
                    })));

        var updates = await service.CheckForUpdatesAsync();

        updates.Should().Equal("beta", profileId);
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnManifestFetchFailure_WhenManifestThrows()
    {
        using var tempRoot = new TempRoot();
        var service = CreateService(tempRoot, new ThrowingHttpMessageHandler("manifest boom"), new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync("base_swfoc");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("manifest_fetch_failed");
        result.Message.Should().Contain("manifest boom");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnProfileMissing_WhenManifestHasNoEntry()
    {
        using var tempRoot = new TempRoot();
        var service = CreateService(
            tempRoot,
            CreateManifestHandler(new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>())),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync("base_swfoc");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("profile_missing_in_manifest");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnShaMismatch_WhenDownloadedPackageHashDiffers()
    {
        using var tempRoot = new TempRoot();
        var profileId = "base_swfoc";
        var zipBytes = BuildZipWithProfile(profileId, BuildValidProfileJson("sha"));
        var service = CreateService(
            tempRoot,
            CreatePackageHandler(
                new ProfileManifestEntry(profileId, "1.2.3", new string('a', 64), $"https://example.invalid/{profileId}.zip", "1.0.0"),
                zipBytes),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("sha_mismatch");
        result.Message.Should().Contain("Expected");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldReturnProfileJsonMissing_WhenPackageLacksProfile()
    {
        using var tempRoot = new TempRoot();
        var profileId = "base_swfoc";
        var zipBytes = BuildZipWithFile("profiles/not-the-profile.json", "{\"id\":\"other\"}");
        var sha = ComputeSha256(zipBytes);
        var service = CreateService(
            tempRoot,
            CreatePackageHandler(
                new ProfileManifestEntry(profileId, "1.2.3", sha, $"https://example.invalid/{profileId}.zip", "1.0.0"),
                zipBytes),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("profile_json_missing");
    }

    [Fact]
    public async Task RollbackLastInstallAsync_ShouldReturnBackupNotFound_WhenNoBackupExists()
    {
        using var tempRoot = new TempRoot();
        var service = CreateService(tempRoot, CreateManifestHandler(new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>())), new StubProfileRepository());

        var rollback = await service.RollbackLastInstallAsync("base_swfoc");

        rollback.Restored.Should().BeFalse();
        rollback.ReasonCode.Should().Be("backup_not_found");
    }

    private static GitHubProfileUpdateService CreateService(
        TempRoot tempRoot,
        HttpMessageHandler handler,
        StubProfileRepository repository)
    {
        return new GitHubProfileUpdateService(
            new HttpClient(handler),
            new ProfileRepositoryOptions
            {
                ProfilesRootPath = tempRoot.ProfilesRoot,
                ManifestFileName = "manifest.json",
                DownloadCachePath = tempRoot.CacheRoot,
                RemoteManifestUrl = "https://example.invalid/manifest.json"
            },
            repository);
    }

    private static HttpMessageHandler CreateManifestHandler(ProfileManifest manifest)
    {
        var manifestJson = JsonSerializer.Serialize(manifest);
        return new StubHttpMessageHandler(
            new Dictionary<string, (string ContentType, byte[] Body)>
            {
                ["https://example.invalid/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson))
            });
    }

    private static HttpMessageHandler CreatePackageHandler(ProfileManifestEntry entry, byte[] zipBytes)
    {
        var manifest = new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, new[] { entry });
        var manifestJson = JsonSerializer.Serialize(manifest);
        return new StubHttpMessageHandler(
            new Dictionary<string, (string ContentType, byte[] Body)>
            {
                ["https://example.invalid/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson)),
                [entry.DownloadUrl] = ("application/zip", zipBytes)
            });
    }

    private static string BuildValidProfileJson(string displayName)
    {
        return $$"""
        {"id":"base_swfoc","displayName":"{{displayName}}","inherits":"base_swfoc","exeTarget":"Swfoc","steamWorkshopId":null,"signatureSets":[],"fallbackOffsets":{},"actions":{},"featureFlags":{},"catalogSources":[],"saveSchemaId":"base_swfoc_steam_v1","helperModHooks":[]}
        """;
    }

    private static byte[] BuildZipWithProfile(string profileId, string profileJson)
        => BuildZipWithFile($"profiles/{profileId}.json", profileJson);

    private static byte[] BuildZipWithFile(string path, string content)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(path);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: false);
            writer.Write(content);
        }

        return memory.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, (string ContentType, byte[] Body)> _responses;

        public StubHttpMessageHandler(IReadOnlyDictionary<string, (string ContentType, byte[] Body)> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            var key = request.RequestUri!.ToString();
            if (!_responses.TryGetValue(key, out var payload))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found")
                });
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload.Body)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(payload.ContentType);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _message;

        public ThrowingHttpMessageHandler(string message)
        {
            _message = message;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new HttpRequestException(_message);
        }
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly ProfileManifest _manifest;

        public StubProfileRepository(ProfileManifest? manifest = null)
        {
            _manifest = manifest ?? new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>());
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(_manifest);
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken = default)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), $"swfoc-profile-update-sweep-{Guid.NewGuid():N}");
            ProfilesRoot = Path.Combine(Root, "default");
            CacheRoot = Path.Combine(Root, "cache");
            Directory.CreateDirectory(Path.Combine(ProfilesRoot, "profiles"));
            Directory.CreateDirectory(CacheRoot);
        }

        public string Root { get; }

        public string ProfilesRoot { get; }

        public string CacheRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
