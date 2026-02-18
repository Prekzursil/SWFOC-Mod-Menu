using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using System.Text.Json;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SaveCorpusRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Codec_ShouldRoundTripForShippedSchemaCorpusFixtures()
    {
        var root = TestPaths.FindRepoRoot();
        var fixtureDir = Path.Combine(root, "tools", "fixtures", "save-corpus");
        var manifestPath = Path.Combine(fixtureDir, "manifest.json");
        File.Exists(manifestPath).Should().BeTrue("save corpus fixtures must be tracked for all shipped schemas");

        var manifest = JsonSerializer.Deserialize<SaveCorpusManifest>(await File.ReadAllTextAsync(manifestPath), JsonOptions);
        manifest.Should().NotBeNull();
        manifest!.SchemaVersion.Should().Be("1.0");
        manifest.Fixtures.Should().NotBeNullOrEmpty();

        var options = new SaveOptions
        {
            SchemaRootPath = Path.Combine(root, "profiles", "default", "schemas")
        };

        var codec = new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);
        foreach (var fixtureName in manifest.Fixtures)
        {
            var fixturePath = Path.Combine(fixtureDir, fixtureName);
            File.Exists(fixturePath).Should().BeTrue();
            var fixture = JsonSerializer.Deserialize<SaveCorpusFixture>(await File.ReadAllTextAsync(fixturePath), JsonOptions);
            fixture.Should().NotBeNull();
            fixture!.SyntheticByteLength.Should().BeGreaterThan(0);

            var tempPath = Path.Combine(Path.GetTempPath(), $"swfoc-corpus-{fixture.SchemaId}-{Guid.NewGuid():N}.sav");
            try
            {
                await File.WriteAllBytesAsync(tempPath, new byte[fixture.SyntheticByteLength]);
                var doc = await codec.LoadAsync(tempPath, fixture.SchemaId);
                var validation = await codec.ValidateAsync(doc);
                validation.IsValid.Should().BeTrue();

                var roundTrip = await codec.RoundTripCheckAsync(doc);
                roundTrip.Should().BeTrue();
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    private sealed record SaveCorpusManifest(string SchemaVersion, string[] Fixtures);

    private sealed record SaveCorpusFixture(string SchemaId, int SyntheticByteLength, string Description);
}
