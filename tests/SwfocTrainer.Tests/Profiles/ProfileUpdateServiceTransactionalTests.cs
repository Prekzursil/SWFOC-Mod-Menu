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

public sealed class ProfileUpdateServiceTransactionalTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnRemoteProfilesWithDifferentVersions()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-profile-check-{Guid.NewGuid():N}");

        try
        {
            var profileId = "base_swfoc";
            Directory.CreateDirectory(Path.Combine(tempRoot, "default", "profiles"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "cache"));

            var service = new GitHubProfileUpdateService(
                new HttpClient(CreateHttpHandler(profileId, BuildZipWithProfile(profileId, BuildProfileJson("new")), ComputeSha256(BuildZipWithProfile(profileId, BuildProfileJson("new"))))),
                new ProfileRepositoryOptions
                {
                    ProfilesRootPath = Path.Combine(tempRoot, "default"),
                    ManifestFileName = "manifest.json",
                    DownloadCachePath = Path.Combine(tempRoot, "cache"),
                    RemoteManifestUrl = "https://example.invalid/manifest.json"
                },
                new StubProfileRepository(BuildManifest(profileId, "1.0.0")));

            var updates = await service.CheckForUpdatesAsync(CancellationToken.None);

            updates.Should().ContainSingle(profileId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldInstallAndRollback()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-profile-update-{Guid.NewGuid():N}");

        try
        {
            var setup = await CreateInstallSetupAsync(tempRoot);
            var install = await setup.Service.InstallProfileTransactionalAsync(setup.ProfileId);
            AssertInstallResult(install);

            var updatedJson = await File.ReadAllTextAsync(setup.ExistingPath);
            updatedJson.Should().Contain("\"displayName\":\"new\"");

            var rollback = await setup.Service.RollbackLastInstallAsync(setup.ProfileId);
            rollback.Restored.Should().BeTrue();

            var rolledBackJson = await File.ReadAllTextAsync(setup.ExistingPath);
            rolledBackJson.Should().Contain("\"displayName\":\"old\"");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFail_WhenProfileMissingFromManifest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-profile-missing-{Guid.NewGuid():N}");

        try
        {
            var setup = await CreateInstallSetupAsync(tempRoot);
            var handler = CreateManifestOnlyHandler("other_profile", version: "1.2.3");
            var service = new GitHubProfileUpdateService(
                new HttpClient(handler),
                new ProfileRepositoryOptions
                {
                    ProfilesRootPath = Path.Combine(tempRoot, "default"),
                    ManifestFileName = "manifest.json",
                    DownloadCachePath = Path.Combine(tempRoot, "cache"),
                    RemoteManifestUrl = "https://example.invalid/manifest.json"
                },
                new StubProfileRepository());

            var install = await service.InstallProfileTransactionalAsync(setup.ProfileId);

            install.Succeeded.Should().BeFalse();
            install.Message.Should().Contain("not present in remote manifest");
            install.ReasonCode.Should().Be("profile_missing_in_manifest");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFail_WhenPackageHashDoesNotMatchManifest()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-profile-hash-{Guid.NewGuid():N}");

        try
        {
            var setup = await CreateInstallSetupAsync(tempRoot);
            var zipBytes = BuildZipWithProfile(setup.ProfileId, BuildProfileJson(displayName: "new"));
            var service = new GitHubProfileUpdateService(
                new HttpClient(CreateHttpHandler(setup.ProfileId, zipBytes, "deadbeef")),
                new ProfileRepositoryOptions
                {
                    ProfilesRootPath = Path.Combine(tempRoot, "default"),
                    ManifestFileName = "manifest.json",
                    DownloadCachePath = Path.Combine(tempRoot, "cache"),
                    RemoteManifestUrl = "https://example.invalid/manifest.json"
                },
                new StubProfileRepository());

            var install = await service.InstallProfileTransactionalAsync(setup.ProfileId);

            install.Succeeded.Should().BeFalse();
            install.ReasonCode.Should().Be("sha_mismatch");
            install.Message.Should().Contain("Expected");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task<InstallSetup> CreateInstallSetupAsync(string tempRoot)
    {
        var profilesRoot = Path.Combine(tempRoot, "default");
        var profilesDir = Path.Combine(profilesRoot, "profiles");
        var cacheDir = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(profilesDir);
        Directory.CreateDirectory(cacheDir);

        var profileId = "base_swfoc";
        var existingPath = Path.Combine(profilesDir, $"{profileId}.json");
        await File.WriteAllTextAsync(existingPath, BuildProfileJson(displayName: "old"));

        var zipBytes = BuildZipWithProfile(profileId, BuildProfileJson(displayName: "new"));
        var sha = ComputeSha256(zipBytes);
        var service = new GitHubProfileUpdateService(
            new HttpClient(CreateHttpHandler(profileId, zipBytes, sha)),
            new ProfileRepositoryOptions
            {
                ProfilesRootPath = profilesRoot,
                ManifestFileName = "manifest.json",
                DownloadCachePath = cacheDir,
                RemoteManifestUrl = "https://example.invalid/manifest.json"
            },
            new StubProfileRepository());
        return new InstallSetup(profileId, existingPath, service);
    }

    private static string BuildProfileJson(string displayName)
    {
        return $"{{\"id\":\"base_swfoc\",\"displayName\":\"{displayName}\",\"inherits\":null,\"exeTarget\":\"Swfoc\",\"steamWorkshopId\":null,\"signatureSets\":[{{\"name\":\"x\",\"gameBuild\":\"x\",\"signatures\":[]}}],\"fallbackOffsets\":{{}},\"actions\":{{}},\"featureFlags\":{{}},\"catalogSources\":[],\"saveSchemaId\":\"base_swfoc_steam_v1\",\"helperModHooks\":[]}}";
    }

    private static StubHttpMessageHandler CreateHttpHandler(string profileId, byte[] zipBytes, string sha)
    {
        var manifestJson = JsonSerializer.Serialize(new
        {
            version = "1.0.0",
            publishedAt = "2026-01-01T00:00:00Z",
            profiles = new[]
            {
                new
                {
                    id = profileId,
                    version = "1.2.3",
                    sha256 = sha,
                    downloadUrl = $"https://example.invalid/{profileId}.zip",
                    minAppVersion = "1.0.0",
                    description = "test"
                }
            }
        });

        return new StubHttpMessageHandler(new Dictionary<string, (string ContentType, byte[] Body)>
        {
            ["https://example.invalid/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson)),
            [$"https://example.invalid/{profileId}.zip"] = ("application/zip", zipBytes)
        });
    }

    private static StubHttpMessageHandler CreateManifestOnlyHandler(string profileId, string version)
    {
        var manifestJson = JsonSerializer.Serialize(new
        {
            version = "1.0.0",
            publishedAt = "2026-01-01T00:00:00Z",
            profiles = new[]
            {
                new
                {
                    id = profileId,
                    version,
                    sha256 = "abc",
                    downloadUrl = $"https://example.invalid/{profileId}.zip",
                    minAppVersion = "1.0.0",
                    description = "test"
                }
            }
        });

        return new StubHttpMessageHandler(new Dictionary<string, (string ContentType, byte[] Body)>
        {
            ["https://example.invalid/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson))
        });
    }

    private static ProfileManifest BuildManifest(string profileId, string version)
    {
        return new ProfileManifest(
            Version: "1.0.0",
            PublishedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Profiles:
            [
                new ProfileManifestEntry(
                    Id: profileId,
                    Version: version,
                    Sha256: "abc",
                    DownloadUrl: $"https://example.invalid/{profileId}.zip",
                    MinAppVersion: "1.0.0",
                    Description: "local")
            ]);
    }

    private static void AssertInstallResult(ProfileInstallResult install)
    {
        install.Succeeded.Should().BeTrue();
        File.Exists(install.InstalledPath).Should().BeTrue();
        install.BackupPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(install.BackupPath!).Should().BeTrue();
        install.ReceiptPath.Should().NotBeNullOrWhiteSpace();
        File.Exists(install.ReceiptPath!).Should().BeTrue();
    }

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

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly ProfileManifest _manifest;

        public StubProfileRepository()
            : this(BuildManifest("base_swfoc", "1.0.0"))
        {
        }

        public StubProfileRepository(ProfileManifest manifest)
        {
            _manifest = manifest;
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

    private sealed record InstallSetup(string ProfileId, string ExistingPath, GitHubProfileUpdateService Service);
}
