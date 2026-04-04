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

public sealed class ProfileUpdateEdgeCoverageTests
{
    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenRemoteManifestUrlIsMissing()
    {
        using var tempRoot = new TempRoot();
        var service = CreateService(
            tempRoot,
            new StubHttpMessageHandler(new Dictionary<string, StubHttpResponse>(StringComparer.OrdinalIgnoreCase)),
            new StubProfileRepository(),
            remoteManifestUrl: null);

        var result = await service.InstallProfileTransactionalAsync("base_swfoc");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("remote_manifest_not_configured");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenRemoteManifestPayloadIsNull()
    {
        using var tempRoot = new TempRoot();
        var service = CreateService(
            tempRoot,
            new StubHttpMessageHandler(new Dictionary<string, StubHttpResponse>(StringComparer.OrdinalIgnoreCase)
            {
                [ManifestUrl] = StubHttpResponse.Json("null")
            }),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync("base_swfoc");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("manifest_empty");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenPackageDownloadThrows()
    {
        using var tempRoot = new TempRoot();
        const string profileId = "base_swfoc";
        var entry = CreateManifestEntry(profileId, version: "1.2.3", sha256: new string('0', 64));
        var service = CreateService(
            tempRoot,
            new StubHttpMessageHandler(new Dictionary<string, StubHttpResponse>(StringComparer.OrdinalIgnoreCase)
            {
                [ManifestUrl] = StubHttpResponse.Json(JsonSerializer.Serialize(new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, new[] { entry }))),
                [entry.DownloadUrl] = StubHttpResponse.Throw(new HttpRequestException("download boom"))
            }),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("download_failed");
        result.Message.Should().Contain("download boom");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenPackageExtractionFails()
    {
        using var tempRoot = new TempRoot();
        const string profileId = "base_swfoc";
        var invalidZipBytes = Encoding.UTF8.GetBytes("definitely-not-a-zip");
        var entry = CreateManifestEntry(profileId, version: "2.0.0", sha256: ComputeSha256(invalidZipBytes));
        var service = CreateService(
            tempRoot,
            CreatePackageHandler(entry, invalidZipBytes),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("extract_failed");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenProfilePayloadDeserializesToNull()
    {
        using var tempRoot = new TempRoot();
        const string profileId = "base_swfoc";
        var zipBytes = BuildZipWithProfile(profileId, "null");
        var entry = CreateManifestEntry(profileId, version: "3.0.0", sha256: ComputeSha256(zipBytes));
        var service = CreateService(
            tempRoot,
            CreatePackageHandler(entry, zipBytes),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("profile_deserialize_failed");
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenDownloadedProfileValidationFails()
    {
        using var tempRoot = new TempRoot();
        const string profileId = "base_swfoc";
        var invalidProfileJson = """
        {"id":" ","displayName":"broken","inherits":"base_swfoc","exeTarget":"Swfoc","steamWorkshopId":null,"signatureSets":[],"fallbackOffsets":{},"actions":{},"featureFlags":{},"catalogSources":[],"saveSchemaId":"base_swfoc_steam_v1","helperModHooks":[]}
        """;
        var zipBytes = BuildZipWithProfile(profileId, invalidProfileJson);
        var entry = CreateManifestEntry(profileId, version: "4.0.0", sha256: ComputeSha256(zipBytes));
        var service = CreateService(
            tempRoot,
            CreatePackageHandler(entry, zipBytes),
            new StubProfileRepository());

        var result = await service.InstallProfileTransactionalAsync(profileId);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("profile_validation_failed");
    }

    private const string ManifestUrl = "https://example.invalid/manifest.json";

    private static GitHubProfileUpdateService CreateService(
        TempRoot tempRoot,
        HttpMessageHandler handler,
        StubProfileRepository repository,
        string? remoteManifestUrl = ManifestUrl)
    {
        return new GitHubProfileUpdateService(
            new HttpClient(handler),
            new ProfileRepositoryOptions
            {
                ProfilesRootPath = tempRoot.ProfilesRoot,
                ManifestFileName = "manifest.json",
                DownloadCachePath = tempRoot.CacheRoot,
                RemoteManifestUrl = remoteManifestUrl
            },
            repository);
    }

    private static HttpMessageHandler CreatePackageHandler(ProfileManifestEntry entry, byte[] zipBytes)
    {
        return new StubHttpMessageHandler(new Dictionary<string, StubHttpResponse>(StringComparer.OrdinalIgnoreCase)
        {
            [ManifestUrl] = StubHttpResponse.Json(JsonSerializer.Serialize(new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, new[] { entry }))),
            [entry.DownloadUrl] = StubHttpResponse.Bytes("application/zip", zipBytes)
        });
    }

    private static ProfileManifestEntry CreateManifestEntry(string profileId, string version, string sha256)
        => new(profileId, version, sha256, $"https://example.invalid/{profileId}.zip", "1.0.0");

    private static byte[] BuildZipWithProfile(string profileId, string profileJson)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"profiles/{profileId}.json");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: false);
            writer.Write(profileJson);
        }

        return memory.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new ProfileManifest("1.0.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
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

    private readonly record struct StubHttpResponse(string ContentType, byte[] Body, Exception? Error)
    {
        public static StubHttpResponse Json(string payload)
            => new("application/json", Encoding.UTF8.GetBytes(payload), null);

        public static StubHttpResponse Bytes(string contentType, byte[] body)
            => new(contentType, body, null);

        public static StubHttpResponse Throw(Exception error)
            => new("application/octet-stream", Array.Empty<byte>(), error);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, StubHttpResponse> _responses;

        public StubHttpMessageHandler(IReadOnlyDictionary<string, StubHttpResponse> responses)
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

            if (payload.Error is not null)
            {
                throw payload.Error;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload.Body)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(payload.ContentType);
            return Task.FromResult(response);
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"swfoc-profile-update-edge-{Guid.NewGuid():N}");
            ProfilesRoot = Path.Combine(RootPath, "default");
            CacheRoot = Path.Combine(RootPath, "cache");
            Directory.CreateDirectory(ProfilesRoot);
            Directory.CreateDirectory(CacheRoot);
        }

        public string RootPath { get; }

        public string ProfilesRoot { get; }

        public string CacheRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
