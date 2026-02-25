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
    public async Task InstallProfileTransactionalAsync_ShouldInstallAndRollback()
    {
        var workspace = CreateWorkspace();

        try
        {
            const string profileId = "base_swfoc";
            var existingPath = Path.Combine(workspace.ProfilesDir, $"{profileId}.json");
            await File.WriteAllTextAsync(existingPath, BuildProfileJson("old"));

            var zipBytes = BuildZipWithProfile(profileId, BuildProfileJson("new"));
            var service = CreateService(workspace, profileId, zipBytes);
            var install = await service.InstallProfileTransactionalAsync(profileId);

            install.Succeeded.Should().BeTrue();
            File.Exists(install.InstalledPath).Should().BeTrue();
            install.BackupPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(install.BackupPath!).Should().BeTrue();
            install.ReceiptPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(install.ReceiptPath!).Should().BeTrue();

            var updatedJson = await File.ReadAllTextAsync(existingPath);
            updatedJson.Should().Contain("\"displayName\":\"new\"");

            var rollback = await service.RollbackLastInstallAsync(profileId);
            rollback.Restored.Should().BeTrue();

            var rolledBackJson = await File.ReadAllTextAsync(existingPath);
            rolledBackJson.Should().Contain("\"displayName\":\"old\"");
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenArchiveContainsDriveQualifiedPath()
    {
        var workspace = CreateWorkspace();

        try
        {
            const string profileId = "base_swfoc";
            var zipBytes = BuildZipWithEntries(new Dictionary<string, string>
            {
                [$"profiles/{profileId}.json"] = BuildProfileJson("new"),
                ["C:evil.txt"] = "bad"
            });
            var service = CreateService(workspace, profileId, zipBytes);
            var result = await service.InstallProfileTransactionalAsync(profileId);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("extract_failed");
            result.Message.Should().Contain("drive-qualified");
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task InstallProfileTransactionalAsync_ShouldFailWhenArchiveContainsTraversalPath()
    {
        var workspace = CreateWorkspace();

        try
        {
            const string profileId = "base_swfoc";
            var zipBytes = BuildZipWithEntries(new Dictionary<string, string>
            {
                [$"profiles/{profileId}.json"] = BuildProfileJson("new"),
                ["../escape.txt"] = "bad"
            });
            var service = CreateService(workspace, profileId, zipBytes);
            var result = await service.InstallProfileTransactionalAsync(profileId);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("extract_failed");
            result.Message.Should().Contain("escapes extraction root");
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    private static ProfileUpdateTestWorkspace CreateWorkspace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-profile-update-{Guid.NewGuid():N}");
        var profilesRoot = Path.Combine(tempRoot, "default");
        var profilesDir = Path.Combine(profilesRoot, "profiles");
        var cacheDir = Path.Combine(tempRoot, "cache");
        Directory.CreateDirectory(profilesDir);
        Directory.CreateDirectory(cacheDir);
        return new ProfileUpdateTestWorkspace(tempRoot, profilesRoot, profilesDir, cacheDir);
    }

    private static void DeleteWorkspace(ProfileUpdateTestWorkspace workspace)
    {
        if (Directory.Exists(workspace.TempRoot))
        {
            Directory.Delete(workspace.TempRoot, recursive: true);
        }
    }

    private static GitHubProfileUpdateService CreateService(
        ProfileUpdateTestWorkspace workspace,
        string profileId,
        byte[] zipBytes)
    {
        var sha = ComputeSha256(zipBytes);
        var manifestJson = BuildManifestJson(profileId, sha);
        var handler = new StubHttpMessageHandler(new Dictionary<string, (string ContentType, byte[] Body)>
        {
            ["https://example.invalid/manifest.json"] = ("application/json", Encoding.UTF8.GetBytes(manifestJson)),
            [$"https://example.invalid/{profileId}.zip"] = ("application/zip", zipBytes)
        });
        var client = new HttpClient(handler);
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = workspace.ProfilesRoot,
            ManifestFileName = "manifest.json",
            DownloadCachePath = workspace.CacheDir,
            RemoteManifestUrl = "https://example.invalid/manifest.json"
        };
        return new GitHubProfileUpdateService(client, options, new StubProfileRepository());
    }

    private static string BuildManifestJson(string profileId, string sha256)
        => JsonSerializer.Serialize(new
        {
            version = "1.0.0",
            publishedAt = "2026-01-01T00:00:00Z",
            profiles = new[]
            {
                new
                {
                    id = profileId,
                    version = "1.2.3",
                    sha256,
                    downloadUrl = $"https://example.invalid/{profileId}.zip",
                    minAppVersion = "1.0.0",
                    description = "test"
                }
            }
        });

    private static string BuildProfileJson(string displayName)
        => $"{{\"id\":\"base_swfoc\",\"displayName\":\"{displayName}\",\"inherits\":null,\"exeTarget\":\"Swfoc\",\"steamWorkshopId\":null,\"signatureSets\":[{{\"name\":\"x\",\"gameBuild\":\"x\",\"signatures\":[]}}],\"fallbackOffsets\":{{}},\"actions\":{{}},\"featureFlags\":{{}},\"catalogSources\":[],\"saveSchemaId\":\"base_swfoc_steam_v1\",\"helperModHooks\":[]}}";

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

    private static byte[] BuildZipWithEntries(IReadOnlyDictionary<string, string> entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entrySpec in entries)
            {
                var entry = zip.CreateEntry(entrySpec.Key);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: false);
                writer.Write(entrySpec.Value);
            }
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
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            throw new NotImplementedException();
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

    private sealed record ProfileUpdateTestWorkspace(
        string TempRoot,
        string ProfilesRoot,
        string ProfilesDir,
        string CacheDir);
}
