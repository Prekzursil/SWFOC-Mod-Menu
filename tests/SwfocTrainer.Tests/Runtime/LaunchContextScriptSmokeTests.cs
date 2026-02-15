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

        var pythonArgsPrefix = string.IsNullOrWhiteSpace(python.Value.PrefixArgs)
            ? string.Empty
            : $"{python.Value.PrefixArgs} ";
        var psi = new ProcessStartInfo
        {
            FileName = python.Value.FileName,
            Arguments = $"{pythonArgsPrefix}\"{scriptPath}\" --from-process-json \"{fixturePath}\" --profile-root \"{Path.Combine(root, "profiles", "default")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch
        {
            return;
        }

        if (process is null)
        {
            return;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(20000);

        process.ExitCode.Should().Be(0, $"script stderr: {stderr}");
        var json = JsonNode.Parse(stdout)!.AsObject();
        var results = json["results"]!.AsArray();

        var byName = results
            .Select(node => node!.AsObject())
            .Where(obj => obj["input"]?["name"] is not null)
            .ToDictionary(
                obj => obj["input"]!["name"]!.GetValue<string>(),
                obj => obj,
                StringComparer.OrdinalIgnoreCase);

        byName["roe-steammod"]["profileRecommendation"]!["profileId"]!.GetValue<string>().Should().Be("roe_3447786229_swfoc");
        byName["roe-steammod"]["profileRecommendation"]!["reasonCode"]!.GetValue<string>().Should().Be("steammod_exact_roe");

        byName["aotr-steammod"]["profileRecommendation"]!["profileId"]!.GetValue<string>().Should().Be("aotr_1397421866_swfoc");
        byName["aotr-steammod"]["profileRecommendation"]!["reasonCode"]!.GetValue<string>().Should().Be("steammod_exact_aotr");

        byName["roe-modpath"]["profileRecommendation"]!["profileId"]!.GetValue<string>().Should().Be("roe_3447786229_swfoc");
        byName["roe-modpath"]["profileRecommendation"]!["reasonCode"]!.GetValue<string>().Should().Be("modpath_hint_roe");

        byName["aotr-modpath"]["profileRecommendation"]!["profileId"]!.GetValue<string>().Should().Be("aotr_1397421866_swfoc");
        byName["aotr-modpath"]["profileRecommendation"]!["reasonCode"]!.GetValue<string>().Should().Be("modpath_hint_aotr");

        byName["sweaw-base"]["profileRecommendation"]!["profileId"]!.GetValue<string>().Should().Be("base_sweaw");
        byName["sweaw-base"]["profileRecommendation"]!["reasonCode"]!.GetValue<string>().Should().Be("exe_target_sweaw");

        byName["starwarsg-no-cmd"]["profileRecommendation"]!["profileId"]!.GetValue<string>().Should().Be("base_swfoc");
        byName["starwarsg-no-cmd"]["profileRecommendation"]!["reasonCode"]!.GetValue<string>().Should().Be("foc_safe_starwarsg_fallback");
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
}
