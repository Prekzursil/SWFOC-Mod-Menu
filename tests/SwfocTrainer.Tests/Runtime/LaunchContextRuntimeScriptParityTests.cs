using System.Diagnostics;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class LaunchContextRuntimeScriptParityTests
{
    [Fact]
    public async Task Resolver_And_Script_Should_Match_For_All_Fixture_Cases()
    {
        if (!TryResolveScriptInputs(out var scriptInputs))
        {
            return;
        }

        var scriptByName = RunScriptAndCollectResults(scriptInputs!);
        var fixtureCases = LoadFixtureCasesByName(scriptInputs!.FixturePath);

        var profiles = await LoadProfilesAsync();
        var resolver = new LaunchContextResolver();

        foreach (var (caseName, caseObject) in fixtureCases)
        {
            AssertRuntimeContextMatchesScript(caseName, caseObject, resolver, profiles, scriptByName);
        }
    }

    private static IReadOnlyDictionary<string, JsonObject> LoadFixtureCasesByName(string fixturePath)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
        var cases = fixture["cases"]!.AsArray();

        return cases
            .Select(node => node!.AsObject())
            .Where(obj => obj["name"] is not null)
            .ToDictionary(
                obj => obj["name"]!.GetValue<string>(),
                obj => obj,
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, ScriptRecommendation> RunScriptAndCollectResults(ScriptInputs inputs)
    {
        using var process = TryStartScriptProcess(inputs);
        if (process is null)
        {
            return new Dictionary<string, ScriptRecommendation>(StringComparer.OrdinalIgnoreCase);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(20000);

        process.ExitCode.Should().Be(0, $"script stderr: {stderr}");

        var json = JsonNode.Parse(stdout)!.AsObject();
        var results = json["results"]!.AsArray();
        return BuildScriptRecommendationsByName(results);
    }

    private static Process? TryStartScriptProcess(ScriptInputs inputs)
    {
        var psi = CreateScriptProcessInfo(inputs);
        try
        {
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, ScriptRecommendation> BuildScriptRecommendationsByName(JsonArray results)
    {
        var byName = new Dictionary<string, ScriptRecommendation>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in results)
        {
            var obj = node!.AsObject();
            var caseName = obj["input"]?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(caseName))
            {
                continue;
            }

            byName[caseName] = BuildScriptRecommendation(obj);
        }

        return byName;
    }

    private static ScriptRecommendation BuildScriptRecommendation(JsonObject obj)
    {
        return new ScriptRecommendation(
            ProfileId: ReadStringValue(obj, "profileRecommendation", "profileId"),
            ReasonCode: ReadStringValue(obj, "profileRecommendation", "reasonCode") ?? "unknown",
            LaunchKind: ReadStringValue(obj, "launchContext", "launchKind") ?? "Unknown");
    }

    private static string? ReadStringValue(JsonObject obj, string objectKey, string propertyKey)
    {
        return obj[objectKey]?[propertyKey]?.GetValue<string>();
    }

    private static ProcessMetadata CreateProcessMetadata(string processName, string processPath, string? commandLine)
    {
        var combined = $"{processName} {processPath} {commandLine}".ToLowerInvariant();
        var isStarWarsG = combined.Contains("starwarsg", StringComparison.Ordinal);
        var exeTarget = combined.Contains("sweaw", StringComparison.Ordinal)
            ? ExeTarget.Sweaw
            : (combined.Contains("swfoc", StringComparison.Ordinal) || isStarWarsG
                ? ExeTarget.Swfoc
                : ExeTarget.Unknown);

        return new ProcessMetadata(
            9002,
            processName,
            processPath,
            commandLine,
            exeTarget,
            RuntimeMode.Unknown,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["detectedVia"] = "unit_test",
                ["isStarWarsG"] = isStarWarsG.ToString()
            });
    }

    private static (string FileName, string PrefixArgs)? ResolvePythonLauncher()
    {
        var candidates = new (string FileName, string PrefixArgs)[]
        {
            ("python3", string.Empty),
            ("python", string.Empty),
            ("py", "-3")
        };

        foreach (var candidate in candidates)
        {
            if (CanExecute(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool CanExecute((string FileName, string PrefixArgs) launcher)
    {
        var prefix = string.IsNullOrWhiteSpace(launcher.PrefixArgs) ? string.Empty : $"{launcher.PrefixArgs} ";
        var psi = new ProcessStartInfo
        {
            FileName = launcher.FileName,
            Arguments = $"{prefix}--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveScriptInputs(out ScriptInputs? inputs)
    {
        var root = TestPaths.FindRepoRoot();
        var scriptPath = Path.Combine(root, "tools", "detect-launch-context.py");
        var fixturePath = Path.Combine(root, "tools", "fixtures", "launch_context_cases.json");
        if (!File.Exists(scriptPath) || !File.Exists(fixturePath))
        {
            inputs = null;
            return false;
        }

        var python = ResolvePythonLauncher();
        if (python is null)
        {
            inputs = null;
            return false;
        }

        inputs = new ScriptInputs(root, scriptPath, fixturePath, python.Value);
        return true;
    }

    private static ProcessStartInfo CreateScriptProcessInfo(ScriptInputs inputs)
    {
        var pythonArgsPrefix = string.IsNullOrWhiteSpace(inputs.Python.PrefixArgs)
            ? string.Empty
            : $"{inputs.Python.PrefixArgs} ";
        return new ProcessStartInfo
        {
            FileName = inputs.Python.FileName,
            Arguments = $"{pythonArgsPrefix}\"{inputs.ScriptPath}\" --from-process-json \"{inputs.FixturePath}\" --profile-root \"{Path.Combine(inputs.Root, "profiles", "default")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = inputs.Root
        };
    }

    private static void AssertRuntimeContextMatchesScript(
        string caseName,
        JsonObject caseObject,
        LaunchContextResolver resolver,
        IReadOnlyList<TrainerProfile> profiles,
        IReadOnlyDictionary<string, ScriptRecommendation> scriptByName)
    {
        scriptByName.Should().ContainKey(caseName);
        var scriptResult = scriptByName[caseName];
        var process = CreateProcessMetadata(
            caseObject["processName"]?.GetValue<string>() ?? string.Empty,
            caseObject["processPath"]?.GetValue<string>() ?? string.Empty,
            caseObject["commandLine"]?.GetValue<string>());
        var runtimeContext = resolver.Resolve(process, profiles);
        runtimeContext.Recommendation.ProfileId.Should().Be(scriptResult.ProfileId, $"case={caseName}");
        runtimeContext.Recommendation.ReasonCode.Should().Be(scriptResult.ReasonCode, $"case={caseName}");
        runtimeContext.LaunchKind.ToString().Should().Be(scriptResult.LaunchKind, $"case={caseName}");
    }

    private static async Task<IReadOnlyList<TrainerProfile>> LoadProfilesAsync()
    {
        var root = TestPaths.FindRepoRoot();
        var repo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        });

        var ids = await repo.ListAvailableProfilesAsync();
        var list = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            list.Add(await repo.ResolveInheritedProfileAsync(id));
        }

        return list;
    }

    private sealed record ScriptInputs(
        string Root,
        string ScriptPath,
        string FixturePath,
        (string FileName, string PrefixArgs) Python);

    private sealed record ScriptRecommendation(string? ProfileId, string ReasonCode, string LaunchKind);
}
