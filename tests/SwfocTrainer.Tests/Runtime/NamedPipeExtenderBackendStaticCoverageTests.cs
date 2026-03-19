#pragma warning disable CA1014
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class NamedPipeExtenderBackendStaticCoverageTests
{
    [Fact]
    public void ResolvePipeNameFromEnvironment_ShouldFallbackToDefault_AndTrimExplicitValue()
    {
        const string envKey = "SWFOC_EXTENDER_PIPE_NAME";
        var original = Environment.GetEnvironmentVariable(envKey);
        try
        {
            Environment.SetEnvironmentVariable(envKey, null);
            ((string)InvokeStatic("ResolvePipeNameFromEnvironment")!).Should().Be("SwfocExtenderBridge");

            Environment.SetEnvironmentVariable(envKey, "  CustomPipe  ");
            ((string)InvokeStatic("ResolvePipeNameFromEnvironment")!).Should().Be("CustomPipe");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, original);
        }
    }

    [Theory]
    [InlineData("base_swfoc", true)]
    [InlineData("base_sweaw", true)]
    [InlineData("aotr_123", true)]
    [InlineData("roe_123", true)]
    [InlineData("other_mod", false)]
    [InlineData("", false)]
    public void ShouldSeedProbeDefaults_ShouldMatchProfileRules(string profileId, bool expected)
    {
        var actual = (bool)InvokeStatic("ShouldSeedProbeDefaults", profileId)!;
        actual.Should().Be(expected);
    }

    [Fact]
    public void BuildProbeAnchors_ShouldSeedDefaults_AndMergeMetadataAnchors()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["probeResolvedAnchorsJson"] = "{\"set_credits\":\"0x1234\",\"empty\":\"\"}"
        };

        var process = BuildProcess(42, metadata);
        var anchors = (JsonObject)InvokeStatic("BuildProbeAnchors", "base_swfoc", process)!;

        anchors["set_credits"]!.GetValue<string>().Should().Be("0x1234");
        anchors["freeze_timer"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        anchors.Should().NotContainKey("empty");
    }

    [Fact]
    public void BuildProbeAnchors_ShouldReturnEmpty_WhenProcessIdNotPositive()
    {
        var anchors = (JsonObject)InvokeStatic("BuildProbeAnchors", "base_swfoc", BuildProcess(0, new Dictionary<string, string>()))!;
        anchors.Should().BeEmpty();
    }

    [Fact]
    public void MergeProbeAnchorsFromMetadata_ShouldIgnoreInvalidJson()
    {
        var anchors = new JsonObject
        {
            ["set_credits"] = "probe"
        };

        var process = BuildProcess(42, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["probeResolvedAnchorsJson"] = "not-json"
        });

        InvokeStatic("MergeProbeAnchorsFromMetadata", process, anchors);
        anchors["set_credits"]!.GetValue<string>().Should().Be("probe");
    }

    [Fact]
    public void TryGetProbeAnchorsJson_ShouldReturnFalse_WhenMetadataMissing()
    {
        var process = BuildProcess(42, new Dictionary<string, string>());
        var args = new object?[] { process, string.Empty };

        var resolved = (bool)InvokeStaticWithArgs("TryGetProbeAnchorsJson", args)!;

        resolved.Should().BeFalse();
        args[1].Should().Be(string.Empty);
    }

    [Fact]
    public void TryGetProbeAnchorsJson_ShouldReturnTrue_WhenMetadataPresent()
    {
        var process = BuildProcess(42, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["probeResolvedAnchorsJson"] = "{\"credits\":\"0xBEEF\"}"
        });
        var args = new object?[] { process, string.Empty };

        var resolved = (bool)InvokeStaticWithArgs("TryGetProbeAnchorsJson", args)!;

        resolved.Should().BeTrue();
        args[1]!.ToString().Should().Contain("0xBEEF");
    }

    [Fact]
    public void AppendNonEmptyAnchorValues_ShouldCopyOnlyNonWhitespaceValues()
    {
        var source = new JsonObject
        {
            ["credits"] = "0x1",
            ["blank"] = "   ",
            ["nullish"] = null
        };
        var destination = new JsonObject();

        InvokeStatic("AppendNonEmptyAnchorValues", source, destination);

        destination["credits"]!.GetValue<string>().Should().Be("0x1");
        destination.Should().NotContainKey("blank");
        destination.Should().NotContainKey("nullish");
    }

    [Fact]
    public void ParseResponse_ShouldCreateNoResponseAndInvalidResponseStates()
    {
        var noResponse = InvokeStatic("ParseResponse", "cmd-1", null);
        ReadProperty<string>(noResponse!, "HookState").Should().Be("no_response");

        var invalid = InvokeStatic("ParseResponse", "cmd-2", "null");
        ReadProperty<string>(invalid!, "HookState").Should().Be("invalid_response");
    }

    [Fact]
    public void CreateTimeoutAndUnreachableResults_ShouldSetExpectedHookState()
    {
        var timeout = InvokeStatic("CreateTimeoutResult", "cmd-timeout");
        ReadProperty<string>(timeout!, "HookState").Should().Be("timeout");

        var unreachable = InvokeStatic("CreateUnreachableResult", "cmd-unreach", "boom");
        ReadProperty<string>(unreachable!, "HookState").Should().Be("unreachable");
        ReadProperty<string>(unreachable!, "Message").Should().Contain("boom");
    }

    [Fact]
    public void AddKnownCandidatePaths_ShouldAddExpectedBridgeHostCandidates()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(Path.GetTempPath(), "swfoc-known-root");

        InvokeStatic("AddKnownCandidatePaths", set, root);

        set.Should().Contain(path => path.EndsWith("SwfocExtender.Host.exe", StringComparison.OrdinalIgnoreCase));
        set.Should().Contain(path => path.EndsWith("SwfocExtender.Host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AddDiscoveredNativeBuildCandidates_ShouldCollectExistingFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "swfoc-native-" + Guid.NewGuid().ToString("N"));
        var nativeBin = Path.Combine(tempRoot, "native", "build", "out");
        Directory.CreateDirectory(nativeBin);
        var winHost = Path.Combine(nativeBin, "SwfocExtender.Host.exe");
        var posixHost = Path.Combine(nativeBin, "SwfocExtender.Host");
        File.WriteAllText(winHost, "stub");
        File.WriteAllText(posixHost, "stub");

        try
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            InvokeStatic("AddDiscoveredNativeBuildCandidates", set, tempRoot);

            set.Should().Contain(winHost);
            set.Should().Contain(posixHost);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveBridgeHostPath_ShouldPreferExplicitEnvironmentPath_WhenFileExists()
    {
        const string envKey = "SWFOC_EXTENDER_HOST_PATH";
        var original = Environment.GetEnvironmentVariable(envKey);
        var file = Path.Combine(Path.GetTempPath(), "swfoc-host-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(file, "stub");

        try
        {
            Environment.SetEnvironmentVariable(envKey, file);
            ((string?)InvokeStatic("ResolveBridgeHostPath")).Should().Be(file);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, original);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private static ProcessMetadata BuildProcess(int processId, Dictionary<string, string> metadata)
    {
        return new ProcessMetadata(
            ProcessId: processId,
            ProcessName: "StarWarsG.exe",
            ProcessPath: @"C:\Games\StarWarsG.exe",
            CommandLine: "StarWarsG.exe",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: metadata,
            LaunchContext: null,
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 123,
            WorkshopMatchCount: 0,
            SelectionScore: 1);
    }

    private static object? InvokeStatic(string methodName, params object?[] args)
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, args);
    }

    private static object? InvokeStaticWithArgs(string methodName, object?[] args)
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, args);
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        prop.Should().NotBeNull();
        return (T)prop!.GetValue(instance)!;
    }
}

