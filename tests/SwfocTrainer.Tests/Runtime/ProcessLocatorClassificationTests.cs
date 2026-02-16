using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessLocatorClassificationTests
{
    [Fact]
    public void InferTarget_Should_Not_Classify_Unknown_Process_From_CommandLine_Token_Leakage()
    {
        var target = ProcessLocator.InferTargetForTesting(
            "steamservice",
            @"C:\Program Files\Steam\steamservice.exe",
            "\"D:\\SteamLibrary\\steamapps\\common\\Star Wars Empire at War\\corruption\\swfoc.exe\" LANGUAGE=ENGLISH");

        target.Should().Be(ExeTarget.Unknown);
    }

    [Fact]
    public void InferTarget_Should_Classify_StarWarsG_With_Mod_Markers_As_Swfoc()
    {
        var target = ProcessLocator.InferTargetForTesting(
            "StarWarsG",
            @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption\StarWarsG.exe",
            "StarWarsG.exe LANGUAGE=ENGLISH STEAMMOD=1397421866");

        target.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void SelectBestMatch_Should_Prefer_StarWarsG_Host_For_Swfoc_Target()
    {
        var processes = new[]
        {
            new ProcessMetadata(
                1010,
                "swfoc",
                @"D:\\Steam\\steamapps\\common\\Star Wars Empire at War\\corruption\\swfoc.exe",
                "swfoc.exe STEAMMOD=1397421866",
                ExeTarget.Swfoc,
                RuntimeMode.Unknown,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["isStarWarsG"] = "false",
                    ["commandLineAvailable"] = "true"
                }),
            new ProcessMetadata(
                2020,
                "StarWarsG",
                @"D:\\Steam\\steamapps\\common\\Star Wars Empire at War\\corruption\\StarWarsG.exe",
                "StarWarsG.exe STEAMMOD=1397421866",
                ExeTarget.Swfoc,
                RuntimeMode.Unknown,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["isStarWarsG"] = "true",
                    ["commandLineAvailable"] = "true"
                })
        };

        var selected = ProcessLocator.SelectBestMatch(ExeTarget.Swfoc, processes);

        selected.Should().NotBeNull();
        selected!.ProcessId.Should().Be(2020);
        selected.ProcessName.Should().Be("StarWarsG");
        selected.Metadata.Should().ContainKey("processSelectionReason");
        selected.Metadata!["processSelectionReason"].Should().Contain("find_best_host_preferred");
    }
}
