#pragma warning disable CA1014
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterGapCoverageTests
{
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    [Fact]
    public void ResolveInitialProcessMatches_ShouldFallbackToStarWarsG_WhenTargetMatchMissing()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with { ExeTarget = ExeTarget.Swfoc };
        var processes = new[]
        {
            BuildProcess(
                processId: 110,
                processName: "StarWarsG",
                path: @"C:\\Games\\StarWarsG.exe",
                commandLine: "STEAMMOD=1125571106",
                exeTarget: ExeTarget.Sweaw,
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["isStarWarsG"] = "true"
                })
        };

        var method = RuntimeAdapterType.GetMethod("ResolveInitialProcessMatches", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);
        var resolved = (ProcessMetadata[])method!.Invoke(adapter, new object?[] { profile, processes })!;

        resolved.Should().HaveCount(1);
        resolved[0].ProcessName.Should().Be("StarWarsG");
    }

    [Fact]
    public void ResolveWorkshopFilteredPool_ShouldUseLooseMatching_WhenStrictMisses()
    {
        var method = RuntimeAdapterType.GetMethod("ResolveWorkshopFilteredPool", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var matches = new[]
        {
            BuildProcess(201, "swfoc", @"C:\\Games\\swfoc.exe", "STEAMMOD=1125571106"),
            BuildProcess(202, "swfoc", @"C:\\Games\\swfoc.exe", "STEAMMOD=2313576303")
        };

        var required = new[] { "1125571106", "999999999" };
        var pool = (ProcessMetadata[])method!.Invoke(null, new object?[] { matches, required })!;

        pool.Should().HaveCount(1);
        pool[0].ProcessId.Should().Be(201);
    }

    [Fact]
    public void ParseSymbolValidationRules_AndCriticalSymbols_ShouldParseMetadataPayloads()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbolValidationRules"] = "[{\"Symbol\":\"credits\",\"IntMin\":0,\"IntMax\":1000}]",
                ["criticalSymbols"] = "credits, game_timer"
            }
        };

        var parseRules = RuntimeAdapterType.GetMethod("ParseSymbolValidationRules", BindingFlags.NonPublic | BindingFlags.Static);
        var parseCritical = RuntimeAdapterType.GetMethod("ParseCriticalSymbols", BindingFlags.NonPublic | BindingFlags.Static);
        parseRules.Should().NotBeNull();
        parseCritical.Should().NotBeNull();

        var rules = (IReadOnlyList<SymbolValidationRule>)parseRules!.Invoke(null, new object?[] { profile })!;
        var critical = (HashSet<string>)parseCritical!.Invoke(null, new object?[] { profile })!;

        rules.Should().ContainSingle();
        rules[0].Symbol.Should().Be("credits");
        rules[0].IntMax.Should().Be(1000);
        critical.Should().Contain(new[] { "credits", "game_timer" });
    }

    [Fact]
    public void ComputeFileSha256_ShouldHashExistingFile()
    {
        var method = RuntimeAdapterType.GetMethod("ComputeFileSha256", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var path = Path.Combine(Path.GetTempPath(), $"swfoc-hash-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7 });
            var hash = (string?)method!.Invoke(null, new object?[] { path });
            hash.Should().NotBeNullOrWhiteSpace();
            hash!.Length.Should().Be(64);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ReadAndWriteAsync_ShouldRoundTripInt32Symbol_WhenAttached()
    {
        var adapter = new AdapterHarness().CreateAdapter(ReflectionCoverageVariantFactory.BuildProfile(), RuntimeMode.Galactic);
        var memoryAccessor = CreateProcessMemoryAccessor();
        using (memoryAccessor as IDisposable)
        {
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", memoryAccessor);
            var address = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(address, 123);
                SetCurrentSessionSymbol(adapter, "credits", address, SymbolValueType.Int32);

                var before = await adapter.ReadAsync<int>("credits", CancellationToken.None);
                before.Should().Be(123);

                await adapter.WriteAsync("credits", 777, CancellationToken.None);
                Marshal.ReadInt32(address).Should().Be(777);
            }
            finally
            {
                Marshal.FreeHGlobal(address);
            }
        }
    }

    [Fact]
    public void BuildProcessContextForCapabilityProbe_ShouldEmitAnchorMetadata_WhenSymbolsExist()
    {
        var profile = ReflectionCoverageVariantFactory.BuildProfile();
        var adapter = new AdapterHarness().CreateAdapter(profile, RuntimeMode.Galactic);

        SetCurrentSessionSymbol(adapter, "credits", (nint)0x1111, SymbolValueType.Int32);

        var process = BuildProcess(
            processId: 301,
            processName: "swfoc",
            path: @"C:\\Games\\swfoc.exe",
            commandLine: "STEAMMOD=1125571106",
            metadata: null);

        var method = RuntimeAdapterType.GetMethod("BuildProcessContextForCapabilityProbe", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var enriched = (ProcessMetadata)method!.Invoke(adapter, new object?[] { process })!;
        enriched.Metadata.Should().NotBeNull();
        enriched.Metadata!.Should().ContainKey("probeResolvedAnchorsJson");
    }

    [Fact]
    public void ExecuteMemoryReadAction_ShouldReturnSuccessAndValidationFailureBranches()
    {
        var adapter = new AdapterHarness().CreateAdapter(ReflectionCoverageVariantFactory.BuildProfile(), RuntimeMode.Galactic);
        var memoryAccessor = CreateProcessMemoryAccessor();
        using (memoryAccessor as IDisposable)
        {
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", memoryAccessor);

            var method = RuntimeAdapterType.GetMethod("ExecuteMemoryReadAction", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Should().NotBeNull();

            var address = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(address, 42);
                var symbol = new SymbolInfo("credits", address, SymbolValueType.Int32, AddressSource.Fallback);

                var passRule = new SymbolValidationRule("credits", IntMin: 0, IntMax: 100);
                var passResult = (ActionExecutionResult)method!.Invoke(adapter, new object?[] { "credits", symbol, passRule, true })!;
                passResult.Succeeded.Should().BeTrue();
                passResult.Diagnostics.Should().ContainKey("validationStatus");

                var failRule = new SymbolValidationRule("credits", IntMin: 0, IntMax: 10);
                var failResult = (ActionExecutionResult)method.Invoke(adapter, new object?[] { "credits", symbol, failRule, true })!;
                failResult.Succeeded.Should().BeFalse();
                failResult.Diagnostics.Should().ContainKey("validationReasonCode");
            }
            finally
            {
                Marshal.FreeHGlobal(address);
            }
        }
    }

    [Fact]
    public void ValidateObservedNumericValueHelpers_ShouldReturnExpectedReasonCodes()
    {
        var validateInt = RuntimeAdapterType.GetMethod("ValidateObservedIntValue", BindingFlags.NonPublic | BindingFlags.Static);
        var validateFloat = RuntimeAdapterType.GetMethod("ValidateObservedFloatValue", BindingFlags.NonPublic | BindingFlags.Static);
        validateInt.Should().NotBeNull();
        validateFloat.Should().NotBeNull();

        var intRule = new SymbolValidationRule("credits", IntMin: 0, IntMax: 10);
        var intOutcome = validateInt!.Invoke(null, new object?[] { "credits", 99L, intRule });
        intOutcome.Should().NotBeNull();
        GetOutcomeFlag(intOutcome!, "IsValid").Should().BeFalse();
        GetOutcomeString(intOutcome!, "ReasonCode").Should().Be("observed_above_max");

        var nonFinite = validateFloat!.Invoke(null, new object?[] { "speed", double.NaN, null });
        nonFinite.Should().NotBeNull();
        GetOutcomeFlag(nonFinite!, "IsValid").Should().BeFalse();
        GetOutcomeString(nonFinite!, "ReasonCode").Should().Be("observed_non_finite");

        var floatRule = new SymbolValidationRule("speed", FloatMin: 0.5, FloatMax: 1.5);
        var floatOutcome = validateFloat.Invoke(null, new object?[] { "speed", 9.9d, floatRule });
        floatOutcome.Should().NotBeNull();
        GetOutcomeFlag(floatOutcome!, "IsValid").Should().BeFalse();
        GetOutcomeString(floatOutcome!, "ReasonCode").Should().Be("observed_above_max");
    }

    [Fact]
    public void EnableDisableCodePatch_ShouldCoverAlreadyPatchedUnexpectedAndForceRestoreBranches()
    {
        var adapter = new AdapterHarness().CreateAdapter(ReflectionCoverageVariantFactory.BuildProfile(), RuntimeMode.Galactic);
        var memoryAccessor = CreateProcessMemoryAccessor();
        using (memoryAccessor as IDisposable)
        {
            RuntimeAdapterExecuteCoverageTests.SetPrivateField(adapter, "_memory", memoryAccessor);

            var address = Marshal.AllocHGlobal(2);
            try
            {
                var enable = RuntimeAdapterType.GetMethod("EnableCodePatch", BindingFlags.NonPublic | BindingFlags.Instance);
                var disable = RuntimeAdapterType.GetMethod("DisableCodePatch", BindingFlags.NonPublic | BindingFlags.Instance);
                enable.Should().NotBeNull();
                disable.Should().NotBeNull();

                var context = CreateCodePatchContext("credits", true, new byte[] { 0x90, 0x90 }, new byte[] { 0x89, 0x01 }, address);

                Marshal.Copy(new byte[] { 0x90, 0x90 }, 0, address, 2);
                var already = (ActionExecutionResult)enable!.Invoke(adapter, new[] { context })!;
                already.Succeeded.Should().BeTrue();
                already.Diagnostics.Should().ContainKey("state");
                already.Diagnostics["state"].Should().Be("already_patched");

                Marshal.Copy(new byte[] { 0x12, 0x34 }, 0, address, 2);
                var unexpected = (ActionExecutionResult)enable.Invoke(adapter, new[] { context })!;
                unexpected.Succeeded.Should().BeFalse();
                unexpected.Message.Should().Contain("unexpected bytes");

                Marshal.Copy(new byte[] { 0x90, 0x90 }, 0, address, 2);
                var forcedRestore = (ActionExecutionResult)disable!.Invoke(adapter, new[] { context })!;
                forcedRestore.Succeeded.Should().BeTrue();
                forcedRestore.Diagnostics.Should().ContainKey("state");
                forcedRestore.Diagnostics["state"].Should().Be("force_restored");
            }
            finally
            {
                Marshal.FreeHGlobal(address);
            }
        }
    }

    private static object CreateProcessMemoryAccessor()
    {
        var memoryType = RuntimeAdapterType.Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor");
        memoryType.Should().NotBeNull();

        var accessor = Activator.CreateInstance(memoryType!, Environment.ProcessId);
        accessor.Should().NotBeNull();
        return accessor!;
    }

    private static ProcessMetadata BuildProcess(
        int processId,
        string processName,
        string path,
        string? commandLine = null,
        ExeTarget exeTarget = ExeTarget.Swfoc,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new ProcessMetadata(
            ProcessId: processId,
            ProcessName: processName,
            ProcessPath: path,
            CommandLine: commandLine,
            ExeTarget: exeTarget,
            Mode: RuntimeMode.Galactic,
            Metadata: metadata);
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

    private static bool GetOutcomeFlag(object outcome, string property)
    {
        var p = outcome.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        p.Should().NotBeNull();
        return (bool)(p!.GetValue(outcome) ?? false);
    }

    private static string GetOutcomeString(object outcome, string property)
    {
        var p = outcome.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        p.Should().NotBeNull();
        return p!.GetValue(outcome)?.ToString() ?? string.Empty;
    }
}

#pragma warning restore CA1014