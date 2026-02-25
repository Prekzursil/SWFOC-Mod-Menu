using System.Diagnostics;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class LaunchContextScriptSmokeTests
{
    [Fact]
    public void DetectLaunchContextScript_Should_Match_Expected_Recommendations_From_Fixtures()
    {
        var root = TestPaths.FindRepoRoot();
        var scriptPath = Path.Combine(root, "tools", "detect-launch-context.py");
        var fixturePath = Path.Combine(root, "tools", "fixtures", "launch_context_cases.json");

        if (!File.Exists(scriptPath) || !File.Exists(fixturePath))
        {
            return;
        }

        var python = ResolvePythonLauncher();
        if (python is null)
        {
            return;
        }

        var runResult = TryRunDetectionScript(root, scriptPath, fixturePath, python.Value);
        if (runResult is null)
        {
            return;
        }

        runResult.ExitCode.Should().Be(0, $"script stderr: {runResult.Stderr}");
        var byName = BuildResultsByName(runResult.Stdout);
        AssertExpectedRecommendations(byName);
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
        var versionArgs = $"{prefix}--version";
        var psi = new ProcessStartInfo
        {
            FileName = launcher.FileName,
            Arguments = versionArgs,
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

    private static ScriptRunResult? TryRunDetectionScript(
        string root,
        string scriptPath,
        string fixturePath,
        (string FileName, string PrefixArgs) python)
    {
        var psi = CreateScriptProcessInfo(root, scriptPath, fixturePath, python);
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(20000);
            return new ScriptRunResult(stdout, stderr, process.ExitCode);
        }
        catch
        {
            return null;
        }
    }

    private static ProcessStartInfo CreateScriptProcessInfo(
        string root,
        string scriptPath,
        string fixturePath,
        (string FileName, string PrefixArgs) python)
    {
        var pythonArgsPrefix = string.IsNullOrWhiteSpace(python.PrefixArgs)
            ? string.Empty
            : $"{python.PrefixArgs} ";
        return new ProcessStartInfo
        {
            FileName = python.FileName,
            Arguments = $"{pythonArgsPrefix}\"{scriptPath}\" --from-process-json \"{fixturePath}\" --profile-root \"{Path.Combine(root, "profiles", "default")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
    }

    private static IReadOnlyDictionary<string, JsonObject> BuildResultsByName(string stdout)
    {
        var json = JsonNode.Parse(stdout)!.AsObject();
        var results = json["results"]!.AsArray();
        return results
            .Select(node => node!.AsObject())
            .Where(obj => obj["input"]?["name"] is not null)
            .ToDictionary(
                obj => obj["input"]!["name"]!.GetValue<string>(),
                obj => obj,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertExpectedRecommendations(IReadOnlyDictionary<string, JsonObject> byName)
    {
        var expected = new Dictionary<string, (string ProfileId, string ReasonCode)>(StringComparer.OrdinalIgnoreCase)
        {
            ["roe-steammod"] = ("roe_3447786229_swfoc", "steammod_exact_roe"),
            ["aotr-steammod"] = ("aotr_1397421866_swfoc", "steammod_exact_aotr"),
            ["roe-modpath"] = ("roe_3447786229_swfoc", "modpath_hint_roe"),
            ["aotr-modpath"] = ("aotr_1397421866_swfoc", "modpath_hint_aotr"),
            ["sweaw-base"] = ("base_sweaw", "exe_target_sweaw"),
            ["starwarsg-no-cmd"] = ("base_swfoc", "foc_safe_starwarsg_fallback"),
            ["mixed-steammod-aotr-modpath-roe"] = ("aotr_1397421866_swfoc", "steammod_exact_aotr"),
            ["mixed-steammod-roe-modpath-aotr"] = ("roe_3447786229_swfoc", "steammod_exact_roe"),
            ["aotr-modpath-spaces"] = ("aotr_1397421866_swfoc", "modpath_hint_aotr"),
            ["roe-modpath-order66"] = ("roe_3447786229_swfoc", "modpath_hint_roe"),
            ["swfoc-base-no-mod"] = ("base_swfoc", "foc_safe_starwarsg_fallback")
        };

        foreach (var (caseName, expectedRecommendation) in expected)
        {
            byName.Should().ContainKey(caseName);
            var recommendation = byName[caseName]["profileRecommendation"]!;
            recommendation["profileId"]!.GetValue<string>()
                .Should().Be(expectedRecommendation.ProfileId, $"case={caseName}");
            recommendation["reasonCode"]!.GetValue<string>()
                .Should().Be(expectedRecommendation.ReasonCode, $"case={caseName}");
        }
    }

    private sealed record ScriptRunResult(string Stdout, string Stderr, int ExitCode);
}
