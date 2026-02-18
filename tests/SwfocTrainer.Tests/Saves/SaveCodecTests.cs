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

        var tempFile = Path.Combine(Path.GetTempPath(), $"swfoc-codec-test-{Guid.NewGuid():N}.sav");
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

    [Fact]
    public async Task Codec_Should_Edit_Float_Double_And_Ascii_Fields()
    {
        var schemaRoot = Path.Combine(Path.GetTempPath(), $"swfoc-codec-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemaRoot);

        var schemaId = "save_codec_extended_types";
        var schemaPath = Path.Combine(schemaRoot, $"{schemaId}.json");
        var schemaJson = """
        {
          "schemaId": "save_codec_extended_types",
          "gameBuild": "test",
          "endianness": "little",
          "rootBlocks": [
            { "id": "header", "name": "Header", "offset": 0, "length": 64, "type": "struct", "fields": ["test_ascii", "test_float", "test_double"] }
          ],
          "fieldDefs": [
            { "id": "test_ascii", "name": "Ascii Field", "valueType": "ascii", "offset": 0, "length": 8, "path": "/header/test_ascii" },
            { "id": "test_float", "name": "Float Field", "valueType": "float", "offset": 8, "length": 4, "path": "/header/test_float" },
            { "id": "test_double", "name": "Double Field", "valueType": "double", "offset": 16, "length": 8, "path": "/header/test_double" }
          ],
          "arrayDefs": [],
          "validationRules": [],
          "checksumRules": []
        }
        """;
        await File.WriteAllTextAsync(schemaPath, schemaJson);

        var options = new SaveOptions { SchemaRootPath = schemaRoot };
        var codec = new BinarySaveCodec(options, NullLogger<BinarySaveCodec>.Instance);

        var tempFile = Path.Combine(Path.GetTempPath(), $"swfoc-codec-extended-{Guid.NewGuid():N}.sav");
        var output = Path.Combine(Path.GetTempPath(), $"swfoc-codec-extended-out-{Guid.NewGuid():N}.sav");
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[128]);
            var doc = await codec.LoadAsync(tempFile, schemaId);

            await codec.EditAsync(doc, "/header/test_ascii", "HELLO");
            await codec.EditAsync(doc, "/header/test_float", 3.5f);
            await codec.EditAsync(doc, "/header/test_double", 9.25d);

            await codec.WriteAsync(doc, output);

            var reloaded = await codec.LoadAsync(output, schemaId);
            reloaded.Root.Children.Should().NotBeNull();
            var fields = reloaded.Root.Children![0].Children!.ToDictionary(x => x.Path, x => x.Value);

            fields["/header/test_ascii"].Should().Be("HELLO");
            fields["/header/test_float"].Should().BeOfType<float>();
            ((float)fields["/header/test_float"]!).Should().BeApproximately(3.5f, 0.0001f);
            fields["/header/test_double"].Should().BeOfType<double>();
            ((double)fields["/header/test_double"]!).Should().BeApproximately(9.25d, 0.0000001d);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (File.Exists(output))
            {
                File.Delete(output);
            }

            if (Directory.Exists(schemaRoot))
            {
                Directory.Delete(schemaRoot, recursive: true);
            }
        }
    }
}
