#pragma warning disable CA1014
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterHighDeficitTargetedTests
{
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    [Fact]
    public void CalibrationCandidateBuilder_ShouldHandleDedupesHitsAndParseErrors()
    {
        var adapter = new AdapterHarness().CreateAdapter(ReflectionCoverageVariantFactory.BuildProfile(), RuntimeMode.Galactic);
        var signatures = new List<(string SetName, SignatureSpec Signature)>
        {
            ("set-a", new SignatureSpec("credits", "F3 0F 2C 40 58", 0, SignatureAddressMode.HitPlusOffset, ValueType: SymbolValueType.Int32)),
            ("set-b", new SignatureSpec("credits-dup", "F3 0F 2C 40 58", 0, SignatureAddressMode.HitPlusOffset, ValueType: SymbolValueType.Int32)),
            ("set-c", new SignatureSpec("invalid", "NOT A PATTERN", 4, SignatureAddressMode.HitPlusOffset, ValueType: SymbolValueType.Int32))
        };

        var moduleBytes = new byte[64];
        moduleBytes[10] = 0xF3;
        moduleBytes[11] = 0x0F;
        moduleBytes[12] = 0x2C;
        moduleBytes[13] = 0x40;
        moduleBytes[14] = 0x58;

        var result = InvokePrivateInstance(adapter, "BuildCalibrationCandidates", signatures, moduleBytes, 8);
        result.Should().BeAssignableTo<IReadOnlyList<RuntimeCalibrationCandidate>>();

        var candidates = (IReadOnlyList<RuntimeCalibrationCandidate>)result!;
        candidates.Should().HaveCount(2, "duplicate signature key should be deduped while invalid pattern still yields parse-error candidate");
        candidates.Should().Contain(x => x.InstructionRva == "0xA");
        candidates.Should().Contain(x => x.Snippet.Contains("pattern_parse_error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProcessContainsWorkshopId_ShouldCheckMetadataAndCommandline()
    {
        var method = RuntimeAdapterType.GetMethod("ProcessContainsWorkshopId", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var withMetadata = new ProcessMetadata(
            ProcessId: 1,
            ProcessName: "swfoc",
            ProcessPath: "C:/swfoc.exe",
            CommandLine: null,
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "1125571106, 2313576303" });

        var withCommandline = withMetadata with
        {
            Metadata = new Dictionary<string, string>(),
            CommandLine = "STEAMMOD=1125571106"
        };

        var miss = withMetadata with
        {
            Metadata = new Dictionary<string, string>(),
            CommandLine = "STEAMMOD=999"
        };

        ((bool)method!.Invoke(null, new object?[] { withMetadata, "2313576303" })!).Should().BeTrue();
        ((bool)method.Invoke(null, new object?[] { withCommandline, "1125571106" })!).Should().BeTrue();
        ((bool)method.Invoke(null, new object?[] { miss, "2313576303" })!).Should().BeFalse();
    }

    [Fact]
    public void CodePatchContextBuilder_ShouldValidatePayloadVariants()
    {
        var adapter = new AdapterHarness().CreateAdapter(ReflectionCoverageVariantFactory.BuildProfile(), RuntimeMode.Galactic);
        SetCurrentSessionSymbol(adapter, "credits", (nint)0x1000, SymbolValueType.Byte);
        var tryBuild = RuntimeAdapterType.GetMethod("TryBuildCodePatchContext", BindingFlags.NonPublic | BindingFlags.Instance);
        tryBuild.Should().NotBeNull();

        var valid = new JsonObject
        {
            ["symbol"] = "credits",
            ["enable"] = true,
            ["patchBytes"] = "90 90",
            ["originalBytes"] = "89 01"
        };

        var validArgs = new object?[] { valid, null, null };
        ((bool)tryBuild!.Invoke(adapter, validArgs)!).Should().BeTrue();
        validArgs[1].Should().NotBeNull();
        validArgs[2].Should().BeNull();

        var missing = new JsonObject { ["symbol"] = "credits" };
        var missingArgs = new object?[] { missing, null, null };
        ((bool)tryBuild.Invoke(adapter, missingArgs)!).Should().BeFalse();
        missingArgs[1].Should().BeNull();
        missingArgs[2].Should().NotBeNull();

        var mismatch = new JsonObject
        {
            ["symbol"] = "credits",
            ["patchBytes"] = "90 90",
            ["originalBytes"] = "89"
        };

        var mismatchArgs = new object?[] { mismatch, null, null };
        ((bool)tryBuild.Invoke(adapter, mismatchArgs)!).Should().BeFalse();
        mismatchArgs[2].Should().NotBeNull();
    }

    [Fact]
    public void CodePatchEnableDisable_ShouldMutateAndRestoreMemory()
    {
        var harness = new AdapterHarness();
        var profile = ReflectionCoverageVariantFactory.BuildProfile();
        var adapter = harness.CreateAdapter(profile, RuntimeMode.Galactic);

        var memoryAccessor = CreateProcessMemoryAccessor();
        using (memoryAccessor as IDisposable)
        {
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", memoryAccessor);

            var address = Marshal.AllocHGlobal(2);
            try
            {
                Marshal.Copy(new byte[] { 0x89, 0x01 }, 0, address, 2);

                var context = CreateCodePatchContext("credits", true, new byte[] { 0x90, 0x90 }, new byte[] { 0x89, 0x01 }, address);
                var enable = InvokePrivateInstance(adapter, "EnableCodePatch", context);
                enable.Should().BeOfType<ActionExecutionResult>();
                ((ActionExecutionResult)enable!).Succeeded.Should().BeTrue();

                var patched = new byte[2];
                Marshal.Copy(address, patched, 0, 2);
                patched.Should().Equal(0x90, 0x90);

                var disable = InvokePrivateInstance(adapter, "DisableCodePatch", context);
                disable.Should().BeOfType<ActionExecutionResult>();
                ((ActionExecutionResult)disable!).Succeeded.Should().BeTrue();

                var restored = new byte[2];
                Marshal.Copy(address, restored, 0, 2);
                restored.Should().Equal(0x89, 0x01);
            }
            finally
            {
                Marshal.FreeHGlobal(address);
            }
        }
    }


    private static void SetCurrentSessionSymbol(object adapter, string symbol, nint address, SymbolValueType valueType)
    {
        var property = RuntimeAdapterType.GetProperty("CurrentSession", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        property.Should().NotBeNull();
        var session = (AttachSession)property!.GetValue(adapter)!;
        var symbolMap = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new SymbolInfo(symbol, address, valueType, AddressSource.Fallback)
        });

        property.SetValue(adapter, session with { Symbols = symbolMap });
    }

    private static object CreateCodePatchContext(string symbol, bool enable, byte[] patchBytes, byte[] originalBytes, nint address)
    {
        var nestedType = RuntimeAdapterType.GetNestedType("CodePatchActionContext", BindingFlags.NonPublic);
        nestedType.Should().NotBeNull();

        var symbolInfo = new SymbolInfo(symbol, address, SymbolValueType.Byte, AddressSource.Fallback);
        var ctor = nestedType!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(x => x.GetParameters().Length == 6);
        return ctor.Invoke(new object?[] { symbol, enable, patchBytes, originalBytes, symbolInfo, address });
    }

    private static object CreateProcessMemoryAccessor()
    {
        var memoryType = RuntimeAdapterType.Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor");
        memoryType.Should().NotBeNull();

        var accessor = Activator.CreateInstance(memoryType!, Environment.ProcessId);
        accessor.Should().NotBeNull();
        return accessor!;
    }

    private static object? InvokePrivateInstance(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull($"private method '{methodName}' should exist");
        return method!.Invoke(instance, args);
    }
}
#pragma warning restore CA1014
