using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

/// <summary>
/// Wave 3 Final coverage for Saves: SaveDiffService (length changed branch, max entries),
/// SaveModels record constructors (SaveSchema, SaveBlockDefinition, SaveFieldDefinition,
/// SaveArrayDefinition, ValidationRule, ChecksumRule, SavePatchCompatibilityResult,
/// SavePatchPreview, SavePatchCompatibility), SavePatchApplyService edge cases.
/// </summary>
public sealed class SavesWave3FinalTests
{
    #region SaveDiffService

    [Fact]
    public void BuildDiffPreview_DifferentLength_ShouldIncludeLengthChangedEntry()
    {
        var original = new byte[] { 0x01, 0x02, 0x03 };
        var current = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var result = SaveDiffService.BuildDiffPreview(original, current, 200);
        result.Should().Contain(s => s.Contains("Length changed"));
    }

    [Fact]
    public void BuildDiffPreview_IdenticalArrays_ShouldReturnEmpty()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var result = SaveDiffService.BuildDiffPreview(data, data, 200);
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildDiffPreview_MaxEntriesReached_ShouldTruncate()
    {
        var original = new byte[100];
        var current = new byte[100];
        for (int i = 0; i < 100; i++) current[i] = 0xFF;
        var result = SaveDiffService.BuildDiffPreview(original, current, 5);
        result.Should().HaveCount(5);
    }

    [Fact]
    public void BuildDiffPreview_DefaultOverload_ShouldUseDefaultMaxEntries()
    {
        var original = new byte[] { 0x01 };
        var current = new byte[] { 0x02 };
        var result = SaveDiffService.BuildDiffPreview(original, current);
        result.Should().HaveCount(1);
    }

    #endregion

    #region Model constructors

    [Fact]
    public void SaveSchema_ShouldStoreAllProperties()
    {
        var blocks = new[] { new SaveBlockDefinition("b1", "Block1", 0, 100, "root") };
        var fields = new[] { new SaveFieldDefinition("f1", "Field1", "int32", 0, 4, "desc", "/path") };
        var arrays = new[] { new SaveArrayDefinition("a1", "Array1", "int32", 0, 10, 4, "/path") };
        var rules = new[] { new ValidationRule("v1", "min_value", "f1", "too low", "error") };
        var checksums = new[] { new ChecksumRule("c1", "crc32", 0, 100, 200, 4) };
        var schema = new SaveSchema("s1", "build1", "little", blocks, fields, arrays, rules, checksums);
        schema.SchemaId.Should().Be("s1");
        schema.Endianness.Should().Be("little");
    }

    [Fact]
    public void SaveBlockDefinition_WithOptionalFields_ShouldStoreAll()
    {
        var block = new SaveBlockDefinition("b1", "Block1", 0, 100, "root", new[] { "f1" }, new[] { "b2" });
        block.Fields.Should().Contain("f1");
        block.Children.Should().Contain("b2");
    }

    [Fact]
    public void SaveBlockDefinition_WithChildren_ShouldWork()
    {
        var block = new SaveBlockDefinition("b1", "Block1", 0, 100, "root", new[] { "f1" }, new[] { "b2" });
        block.Id.Should().Be("b1");
        block.Fields.Should().Contain("f1");
        block.Children.Should().Contain("b2");
    }

    [Fact]
    public void SaveFieldDefinition_ShouldStoreAllProperties()
    {
        var field = new SaveFieldDefinition("f1", "Field1", "float", 10, 4, "A float field", "/root/field");
        field.Description.Should().Be("A float field");
        field.Path.Should().Be("/root/field");
    }

    [Fact]
    public void SaveArrayDefinition_ShouldStoreAllProperties()
    {
        var arr = new SaveArrayDefinition("a1", "Arr1", "byte", 0, 256, 1, "/data/arr");
        arr.ElementType.Should().Be("byte");
        arr.Count.Should().Be(256);
        arr.Stride.Should().Be(1);
        arr.Path.Should().Be("/data/arr");
    }

    [Fact]
    public void ValidationRule_DefaultSeverity_ShouldBeError()
    {
        var rule = new ValidationRule("r1", "range", "target", "msg");
        rule.Severity.Should().Be("error");
    }

    [Fact]
    public void ChecksumRule_ShouldStoreAllProperties()
    {
        var rule = new ChecksumRule("c1", "crc32", 0, 100, 200, 4);
        rule.Algorithm.Should().Be("crc32");
        rule.OutputOffset.Should().Be(200);
        rule.OutputLength.Should().Be(4);
    }

    [Fact]
    public void SavePatchCompatibilityResult_ShouldStoreAllProperties()
    {
        var r = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), new[] { "warn" });
        r.IsCompatible.Should().BeTrue();
        r.SourceHashMatches.Should().BeTrue();
        r.TargetHash.Should().Be("hash");
        r.Warnings.Should().Contain("warn");
    }

    [Fact]
    public void SavePatchPreview_ShouldStoreAllProperties()
    {
        var ops = new[] { new SavePatchOperation(SavePatchOperationKind.SetValue, "/f", "f", "int32", null, 42, 0) };
        var p = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), ops);
        p.IsCompatible.Should().BeTrue();
        p.OperationsToApply.Should().HaveCount(1);
    }

    [Fact]
    public void SavePatchCompatibility_ShouldStoreAllProperties()
    {
        var c = new SavePatchCompatibility(new[] { "p1" }, "schema1", "hint");
        c.AllowedProfileIds.Should().Contain("p1");
        c.RequiredSchemaId.Should().Be("schema1");
        c.SaveBuildHint.Should().Be("hint");
    }

    [Fact]
    public void SavePatchMetadata_ShouldStoreAllProperties()
    {
        var m = new SavePatchMetadata("v1", "p1", "s1", "hash", DateTimeOffset.UtcNow);
        m.SchemaVersion.Should().Be("v1");
        m.SourceHash.Should().Be("hash");
    }

    [Fact]
    public void SavePatchOperation_ShouldStoreAllProperties()
    {
        var op = new SavePatchOperation(SavePatchOperationKind.SetValue, "/field", "field1", "int32", 10, 20, 100);
        op.Kind.Should().Be(SavePatchOperationKind.SetValue);
        op.OldValue.Should().Be(10);
        op.NewValue.Should().Be(20);
        op.Offset.Should().Be(100);
    }

    [Fact]
    public void SavePatchApplyResult_AllOptionalFields_ShouldStoreAll()
    {
        var failure = new SavePatchApplyFailure("reason", "msg", "f1", "/path");
        var result = new SavePatchApplyResult(
            SavePatchApplyClassification.RolledBack, false, "err", "/out", "/bak", "/receipt", failure);
        result.Failure.Should().NotBeNull();
        result.Failure!.ReasonCode.Should().Be("reason");
    }

    [Fact]
    public void SaveRollbackResult_AllOptionalFields_ShouldStoreAll()
    {
        var r = new SaveRollbackResult(true, "ok", "/target", "/bak", "hash123");
        r.Restored.Should().BeTrue();
        r.TargetPath.Should().Be("/target");
        r.BackupPath.Should().Be("/bak");
        r.RestoredHash.Should().Be("hash123");
    }

    #endregion
}
