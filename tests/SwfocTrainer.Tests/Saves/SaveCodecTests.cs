using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SaveCodecTests
{
    [Fact]
    public async Task Codec_Should_Load_Edit_Validate_And_RoundTrip()
    {
        var root = TestPaths.FindRepoRoot();
        var options = new SaveOptions
        {
            SchemaRootPath = Path.Combine(root, "profiles", "default", "schemas")
        };

        var codec = new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);

        var tempFile = Path.GetTempFileName();
        try
        {
            var bytes = new byte[300_000];
            await File.WriteAllBytesAsync(tempFile, bytes);

            var doc = await codec.LoadAsync(tempFile, "base_swfoc_steam_v1");
            await codec.EditAsync(doc, "/economy/credits_empire", 5000);
            await codec.EditAsync(doc, "/hero_state/vader/respawn_timer", 120);

            var validation = await codec.ValidateAsync(doc);
            validation.IsValid.Should().BeTrue();

            var ok = await codec.RoundTripCheckAsync(doc);
            ok.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
