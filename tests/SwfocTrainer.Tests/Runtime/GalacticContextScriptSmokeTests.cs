using System.Diagnostics;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class GalacticContextScriptSmokeTests
{
    [Fact]
    public void GalacticContextScript_Should_Select_Expected_Save_Strategies_From_Fixtures()
    {
        var root = TestPaths.FindRepoRoot();
        var scriptPath = Path.Combine(root, "tools", "live", "drive-galactic-context.py");
        var fixturePath = Path.Combine(root, "tools", "fixtures", "galactic_context_cases.json");
        var profileRoot = Path.Combine(root, "profiles", "default");

        if (!File.Exists(scriptPath) || !File.Exists(fixturePath))
        {
            return;
        }

        var python = ResolvePythonLauncher();
        if (python is null)
        {
            return;
        }

        var runResult = TryRunScript(root, scriptPath, fixturePath, profileRoot, python.Value);
        if (runResult is null)
        {
            return;
        }

        runResult.ExitCode.Should().Be(0, $"script stderr: {runResult.Stderr}");
        var byName = BuildResultsByName(runResult.Stdout);
        AssertExpectedSelections(byName);
    }


    [Fact]
    public void GalacticContextScript_Should_Materialize_Fixture_Save_From_Compatible_Real_Save()
    {
        var root = TestPaths.FindRepoRoot();
        var scriptPath = Path.Combine(root, "tools", "live", "drive-galactic-context.py");
        var profileRoot = Path.Combine(root, "profiles", "default");

        if (!File.Exists(scriptPath))
        {
            return;
        }

        var python = ResolvePythonLauncher();
        if (python is null)
        {
            return;
        }

        var tempRoot = Directory.CreateTempSubdirectory("galactic-context-materialize-");
        try
        {
            var sourceSavePath = Path.Combine(tempRoot.FullName, "aotr_galactic_map.PetroglyphFoC64Save");
            WriteSyntheticGalacticSave(sourceSavePath, campaignMode: 1);

            var fixtureSavePath = Path.Combine(tempRoot.FullName, "swfoc_trainer_live_aotr_galactic.PetroglyphFoC64Save");
            File.Exists(fixtureSavePath).Should().BeFalse();

            var result = TryRunLiveSelectionOnlyScript(
                root,
                scriptPath,
                profileRoot,
                "aotr_1397421866_swfoc",
                tempRoot.FullName,
                materializeFixture: true,
                python.Value);

            result.Should().NotBeNull();
            result!.ExitCode.Should().Be(0, $"script stderr: {result.Stderr}");

            var receipt = JsonNode.Parse(result.Stdout)!.AsObject();
            receipt["result"]!["status"]!.GetValue<string>().Should().Be("ready");
            receipt["result"]!["reasonCode"]!.GetValue<string>().Should().Be("fixture_materialized_from_existing");
            receipt["saveSelection"]!["selectedSaveName"]!.GetValue<string>().Should().Be("swfoc_trainer_live_aotr_galactic.PetroglyphFoC64Save");
            receipt["saveSelection"]!["source"]!.GetValue<string>().Should().Be("fixture_created_from_existing");

            File.Exists(fixtureSavePath).Should().BeTrue("the script should materialize the deterministic fixture save from the compatible source save");
        }
        finally
        {
            try
            {
                tempRoot.Delete(recursive: true);
            }
            catch
            {
                // best-effort cleanup for test temp folder
            }
        }
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

    private static ScriptRunResult? TryRunLiveSelectionOnlyScript(
        string root,
        string scriptPath,
        string profileRoot,
        string profileId,
        string saveRoot,
        bool materializeFixture,
        (string FileName, string PrefixArgs) python)
    {
        var prefix = string.IsNullOrWhiteSpace(python.PrefixArgs)
            ? string.Empty
            : $"{python.PrefixArgs} ";
        var materializeArg = materializeFixture ? " --materialize-fixture" : string.Empty;
        var psi = new ProcessStartInfo
        {
            FileName = python.FileName,
            Arguments =
                $"{prefix}\"{scriptPath}\" --profile-root \"{profileRoot}\" --profile-id \"{profileId}\" --save-root \"{saveRoot}\" --selection-only{materializeArg} --pretty",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };

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

    private static void WriteSyntheticGalacticSave(string path, int campaignMode)
    {
        var data = new byte[512];
        var magic = new byte[] { 0x52, 0x47, 0x4D, 0x48, 0x01, 0x00, 0x00, 0x00 };
        Buffer.BlockCopy(magic, 0, data, 0, magic.Length);
        BitConverter.GetBytes(campaignMode).CopyTo(data, 48);
        File.WriteAllBytes(path, data);
    }

    private static ScriptRunResult? TryRunScript(
        string root,
        string scriptPath,
        string fixturePath,
        string profileRoot,
        (string FileName, string PrefixArgs) python)
    {
        var psi = CreateProcessInfo(root, scriptPath, fixturePath, profileRoot, python);
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

    private static ProcessStartInfo CreateProcessInfo(
        string root,
        string scriptPath,
        string fixturePath,
        string profileRoot,
        (string FileName, string PrefixArgs) python)
    {
        var prefix = string.IsNullOrWhiteSpace(python.PrefixArgs)
            ? string.Empty
            : $"{python.PrefixArgs} ";
        return new ProcessStartInfo
        {
            FileName = python.FileName,
            Arguments =
                $"{prefix}\"{scriptPath}\" --from-fixture-json \"{fixturePath}\" --profile-root \"{profileRoot}\" --pretty",
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
            .Where(obj => obj["caseName"] is not null)
            .ToDictionary(
                obj => obj["caseName"]!.GetValue<string>(),
                obj => obj,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertExpectedSelections(IReadOnlyDictionary<string, JsonObject> byName)
    {
        AssertCase(
            byName,
            "aotr_exact_fixture",
            expectedStatus: "ready",
            expectedReason: "save_selected_existing_fixture",
            expectedSaveName: "swfoc_trainer_live_aotr_galactic.PetroglyphFoC64Save",
            expectedSource: "existing_fixture");

        AssertCase(
            byName,
            "aotr_materialize_fixture_from_existing",
            expectedStatus: "ready",
            expectedReason: "fixture_materialized_from_existing",
            expectedSaveName: "swfoc_trainer_live_aotr_galactic.PetroglyphFoC64Save",
            expectedSource: "fixture_created_from_existing");

        AssertCase(
            byName,
            "aotr_exact_fixture_real_magic",
            expectedStatus: "ready",
            expectedReason: "save_selected_existing_fixture",
            expectedSaveName: "swfoc_trainer_live_aotr_galactic.PetroglyphFoC64Save",
            expectedSource: "existing_fixture");

        AssertCase(
            byName,
            "roe_existing_hint_order66",
            expectedStatus: "ready",
            expectedReason: "save_selected_existing_compatible",
            expectedSaveName: "order66_campaign_slot_01.PetroglyphFoC64Save",
            expectedSource: "existing_compatible");

        AssertCase(
            byName,
            "roe_fixture_required_without_hint",
            expectedStatus: "blocked",
            expectedReason: "fixture_required",
            expectedSaveName: null,
            expectedSource: null);


        AssertCase(
            byName,
            "roe_reject_aotr_fixture_without_roe_hints",
            expectedStatus: "blocked",
            expectedReason: "fixture_required",
            expectedSaveName: null,
            expectedSource: null);

        AssertCase(
            byName,
            "reject_non_galactic_fixture",
            expectedStatus: "blocked",
            expectedReason: "fixture_not_galactic",
            expectedSaveName: null,
            expectedSource: null);
    }

    private static void AssertCase(
        IReadOnlyDictionary<string, JsonObject> byName,
        string caseName,
        string expectedStatus,
        string expectedReason,
        string? expectedSaveName,
        string? expectedSource)
    {
        byName.Should().ContainKey(caseName);
        var receipt = byName[caseName];
        receipt["result"]!["status"]!.GetValue<string>().Should().Be(expectedStatus, $"case={caseName}");
        receipt["result"]!["reasonCode"]!.GetValue<string>().Should().Be(expectedReason, $"case={caseName}");

        var selected = receipt["saveSelection"] as JsonObject;
        if (expectedSaveName is null)
        {
            selected.Should().BeNull($"case={caseName}");
            return;
        }

        selected.Should().NotBeNull($"case={caseName}");
        selected!["selectedSaveName"]!.GetValue<string>().Should().Be(expectedSaveName, $"case={caseName}");
        selected["source"]!.GetValue<string>().Should().Be(expectedSource, $"case={caseName}");
    }

    private sealed record ScriptRunResult(string Stdout, string Stderr, int ExitCode);
}

