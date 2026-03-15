using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SaveInfrastructureCoverageTests
{
    private delegate uint Crc32ComputeDelegate(ReadOnlySpan<byte> data);

    [Fact]
    public void SaveOptions_ShouldExposeExpectedDefaults()
    {
        var options = new SaveOptions();

        options.SchemaRootPath.Should().Contain("profiles");
        options.DefaultSaveRootPath.Should().Contain("Petroglyph");
    }

    [Theory]
    [InlineData("", 0u)]
    [InlineData("123456789", 3421780262u)]
    public void Crc32_Compute_ShouldMatchExpectedValues(string text, uint expected)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);

        InvokeCrc32(bytes).Should().Be(expected);
    }

    [Fact]
    public async Task SaveSchemaRepository_ShouldLoadValidSchema()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var options = new SaveOptions { SchemaRootPath = tempRoot };
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "unit.json"), """
            {
              "schemaId": "unit",
              "gameBuild": "1.0",
              "endianness": "little",
              "rootBlocks": [],
              "fieldDefs": [],
              "arrayDefs": [],
              "validationRules": [],
              "checksumRules": []
            }
            """);

            var schema = await InvokeLoadSchemaAsync(options, "unit");

            schema.SchemaId.Should().Be("unit");
            schema.RootBlocks.Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task SaveSchemaRepository_ShouldThrowWhenSchemaMissing()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var options = new SaveOptions { SchemaRootPath = tempRoot };

            var act = () => InvokeLoadSchemaAsync(options, "missing");

            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task SaveSchemaRepository_ShouldThrowWhenJsonMalformed()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var options = new SaveOptions { SchemaRootPath = tempRoot };
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "broken.json"), "{ not-json }");

            var act = () => InvokeLoadSchemaAsync(options, "broken");

            await act.Should().ThrowAsync<System.Text.Json.JsonException>();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static async Task<SaveSchema> InvokeLoadSchemaAsync(SaveOptions options, string schemaId)
    {
        var assembly = typeof(BinarySaveCodec).Assembly;
        var type = assembly.GetType("SwfocTrainer.Saves.Internal.SaveSchemaRepository", throwOnError: true)!;
        var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [typeof(SaveOptions)], null)!;
        var repository = ctor.Invoke([options]);
        var method = type.GetMethod("LoadSchemaAsync", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

        try
        {
            var task = (Task<SaveSchema>)method.Invoke(repository, [schemaId, CancellationToken.None])!;
            return await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static uint InvokeCrc32(byte[] data)
    {
        var assembly = typeof(BinarySaveCodec).Assembly;
        var type = assembly.GetType("SwfocTrainer.Saves.Checksum.Crc32", throwOnError: true)!;
        var method = type.GetMethod("Compute", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        var compute = (Crc32ComputeDelegate)method.CreateDelegate(typeof(Crc32ComputeDelegate));
        return compute(data);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "swfoctrainer-save-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
