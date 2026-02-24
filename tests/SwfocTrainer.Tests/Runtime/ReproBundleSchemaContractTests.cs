using FluentAssertions;
using System.Text.Json.Nodes;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ReproBundleSchemaContractTests
{
    [Fact]
    public void ReproBundleSchema_Should_Require_ActionStatusDiagnostics()
    {
        var repoRoot = TestPaths.FindRepoRoot();
        var schemaPath = Path.Combine(repoRoot, "tools", "schemas", "repro-bundle.schema.json");
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();

        var required = schema["required"]!
            .AsArray()
            .Select(x => x!.GetValue<string>())
            .ToArray();
        required.Should().Contain("actionStatusDiagnostics");

        var properties = schema["properties"]!.AsObject();
        properties.Should().ContainKey("actionStatusDiagnostics");

        var actionStatus = properties["actionStatusDiagnostics"]!.AsObject();
        var actionStatusRequired = actionStatus["required"]!
            .AsArray()
            .Select(x => x!.GetValue<string>())
            .ToArray();
        actionStatusRequired.Should().Contain(new[] { "status", "source", "summary", "entries" });

        var summaryRequired = actionStatus["properties"]!["summary"]!["required"]!
            .AsArray()
            .Select(x => x!.GetValue<string>())
            .ToArray();
        summaryRequired.Should().Contain(new[] { "total", "passed", "failed", "skipped" });
    }
}
