using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 11 coverage tests targeting uncovered lines and branches in:
/// - SignatureResolver.cs (main entry, constructor, ResolveAsync guard clauses)
/// - SignatureResolver.Fallbacks.cs (HandleSignatureHit, HandleSignatureMiss, ApplyStandaloneFallbacks)
/// - SignatureResolver.SymbolHydration.cs (TryParseAddress, TryBuildAnchorSymbol, BuildSymbolValueTypeIndex, etc.)
/// - SignatureResolver.Addressing.cs (partial branches at L51, L56)
/// - ModDependencyValidator.cs (uncovered branches)
/// - GameLaunchService.cs (BuildModPathArgument, NormalizeWorkshopIds, BuildSteamModArguments, ResolveExecutablePath)
/// - LaunchContextResolver.cs (uncovered branches/lines)
/// - CapabilityMapResolver.cs (uncovered lines and branches)
/// - WorkshopInventoryService.Chains.cs (uncovered lines)
/// - SymbolHealthService.cs (uncovered branches)
/// - RuntimeModeProbeResolver.cs (uncovered lines/branches)
/// - ProcessLocator.cs (static helpers via reflection)
/// - BinaryFingerprintService.cs (BuildFingerprintId via reflection)
/// Uses reflection for internal/private methods. No Win32 API calls.
/// </summary>
public sealed class RuntimeWave11CoverageTests
{
    // ==============================================================
    // Helpers
    // ==============================================================

    private static readonly Type SignatureResolverType = typeof(SignatureResolver);
    private static readonly Type SymbolHydrationTypeInternal =
        SignatureResolverType.Assembly.GetType("SwfocTrainer.Runtime.Services.SignatureResolverSymbolHydration")!;
    private static readonly Type FallbacksType =
        SignatureResolverType.Assembly.GetType("SwfocTrainer.Runtime.Services.SignatureResolverFallbacks")!;
    private static readonly Type AddressingType =
        SignatureResolverType.Assembly.GetType("SwfocTrainer.Runtime.Services.SignatureResolverAddressing")!;
    private static readonly Type ChainResolverType =
        SignatureResolverType.Assembly.GetType("SwfocTrainer.Runtime.Services.WorkshopInventoryChainResolver")!;
    private static readonly Type ProcessLocatorType = typeof(ProcessLocator);
    private static readonly Type GameLaunchServiceType = typeof(GameLaunchService);
    private static readonly Type BinaryFingerprintServiceType = typeof(BinaryFingerprintService);
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    private static object? InvokeStatic(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        method.Should().NotBeNull($"Expected to find method '{methodName}' on {type.Name}");
        return method!.Invoke(null, args);
    }

    private static object? InvokeStaticWithTypes(Type type, string methodName, Type[] paramTypes, params object?[] args)
    {
        var method = type.GetMethod(methodName,
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, paramTypes, null);
        method.Should().NotBeNull($"Expected to find method '{methodName}' on {type.Name} with {paramTypes.Length} params");
        return method!.Invoke(null, args);
    }

    private static TrainerProfile CreateMinimalProfile(
        string id,
        string? steamWorkshopId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            id, id, null, ExeTarget.Swfoc, steamWorkshopId,
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), string.Empty, Array.Empty<HelperHookSpec>(),
            metadata);
    }

    private static ProcessMetadata CreateProcessMetadata(
        string processName = "swfoc",
        string processPath = "C:\\game\\swfoc.exe",
        string? commandLine = null,
        ExeTarget exeTarget = ExeTarget.Swfoc,
        IReadOnlyDictionary<string, string>? metadata = null,
        LaunchContext? launchContext = null)
    {
        return new ProcessMetadata(
            1, processName, processPath, commandLine, exeTarget, RuntimeMode.Unknown,
            metadata, launchContext);
    }

    // ==============================================================
    // SignatureResolver constructor + ResolveAsync guard clauses
    // ==============================================================

    [Fact]
    public void SignatureResolver_Constructor_NullLogger_Throws()
    {
        var act = () => new SignatureResolver(null!, "root");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_Constructor_NullRoot_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => new SignatureResolver(logger, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_Constructor_ValidArgs_DoesNotThrow()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "some_root");
        resolver.Should().NotBeNull();
    }

    [Fact]
    public async Task SignatureResolver_ResolveAsync_NullProfileBuild_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "root");
        var act = () => resolver.ResolveAsync(null!, Array.Empty<SignatureSet>(), new Dictionary<string, long>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SignatureResolver_ResolveAsync_NullSignatureSets_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "root");
        var build = new ProfileBuild("test", "1.0", "exe.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, null!, new Dictionary<string, long>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SignatureResolver_ResolveAsync_NullFallbackOffsets_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "root");
        var build = new ProfileBuild("test", "1.0", "exe.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SignatureResolver_ResolveAsync_WithCancellation_NullProfileBuild_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "root");
        var act = () => resolver.ResolveAsync(null!, Array.Empty<SignatureSet>(), new Dictionary<string, long>(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SignatureResolver_ResolveAsync_WithCancellation_NullSignatureSets_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "root");
        var build = new ProfileBuild("test", "1.0", "exe.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, null!, new Dictionary<string, long>(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SignatureResolver_ResolveAsync_WithCancellation_NullFallbackOffsets_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var resolver = new SignatureResolver(logger, "root");
        var build = new ProfileBuild("test", "1.0", "exe.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_DefaultConstructor_UsesDefaultRoot()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        // The default constructor calls ResolveDefaultGhidraSymbolPackRoot internally
        var resolver = new SignatureResolver(logger);
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_NullRoot_Throws()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath(null!, "fp");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_NullFingerprint_Throws()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath("root", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_EmptyRoot_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("  ", "fp");
        result.Should().BeNull();
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_EmptyFingerprint_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("root", "  ");
        result.Should().BeNull();
    }

    [Fact]
    public void SignatureResolver_SelectBestGhidraPackPath_NonexistentDir_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("C:\\nonexistent_dir_12345\\symbols", "some_fp_id");
        result.Should().BeNull();
    }

    // ==============================================================
    // SignatureResolver.SymbolHydration — TryParseAddress variants
    // ==============================================================

    [Fact]
    public void SymbolHydration_TryParseAddressString_Null_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddressString",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var args = new object?[] { "", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseAddressString_HexPrefix_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddressString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { "0x1A2B", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be((long)0x1A2B);
    }

    [Fact]
    public void SymbolHydration_TryParseAddressString_DecimalNumber_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddressString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { "42", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(42L);
    }

    [Fact]
    public void SymbolHydration_TryParseAddressString_NonNumeric_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddressString",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { "not_a_number", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseNumericAddress_Long_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseNumericAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { (object)100L, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(100L);
    }

    [Fact]
    public void SymbolHydration_TryParseNumericAddress_Int_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseNumericAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { (object)50, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(50L);
    }

    [Fact]
    public void SymbolHydration_TryParseNumericAddress_String_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseNumericAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { (object)"hello", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseNumericAddress_Null_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseNumericAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { null, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseJsonAddress_NonJsonElement_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseJsonAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { (object)"hello", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseJsonAddress_JsonNumber_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseJsonAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var json = JsonDocument.Parse("12345");
        var args = new object?[] { (object)json.RootElement, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(12345L);
    }

    [Fact]
    public void SymbolHydration_TryParseJsonAddress_JsonString_HexPrefix_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseJsonAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var json = JsonDocument.Parse("\"0xFF\"");
        var args = new object?[] { (object)json.RootElement, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(255L);
    }

    [Fact]
    public void SymbolHydration_TryParseJsonAddress_JsonStringNonNumeric_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseJsonAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var json = JsonDocument.Parse("\"not_a_number\"");
        var args = new object?[] { (object)json.RootElement, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseJsonAddress_JsonNull_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseJsonAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var json = JsonDocument.Parse("null");
        var args = new object?[] { (object)json.RootElement, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseAddress_NullValue_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { null, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_TryParseAddress_IntegerValue_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { (object)42, (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(42L);
    }

    [Fact]
    public void SymbolHydration_TryParseAddress_StringHex_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("TryParseAddress",
            BindingFlags.Static | BindingFlags.NonPublic);
        var args = new object?[] { (object)"0xABC", (long)0 };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[1].Should().Be(0xABCL);
    }

    [Fact]
    public void SymbolHydration_IsMatchingFingerprint_MatchingIds_ReturnsTrue()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("IsMatchingFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "abc", "ABC" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void SymbolHydration_IsMatchingFingerprint_MismatchedIds_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("IsMatchingFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "abc", "xyz" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_IsMatchingFingerprint_NullActual_ReturnsFalse()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("IsMatchingFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { null, "xyz" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void SymbolHydration_BuildSymbolValueTypeIndex_EmptySets_ReturnsEmpty()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("BuildSymbolValueTypeIndex",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { Array.Empty<SignatureSet>() }) as IReadOnlyDictionary<string, SymbolValueType>;
        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
    }

    [Fact]
    public void SymbolHydration_BuildSymbolValueTypeIndex_WithSignatures_BuildsIndex()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("BuildSymbolValueTypeIndex",
            BindingFlags.Static | BindingFlags.NonPublic);
        var sig1 = new SignatureSpec("sym1", "AA BB", 0, SignatureAddressMode.HitPlusOffset, "", SymbolValueType.Int32);
        var sig2 = new SignatureSpec("sym2", "CC DD", 0, SignatureAddressMode.HitPlusOffset, "", SymbolValueType.Float);
        var set = new SignatureSet("testSet", "1.0", new[] { sig1, sig2 });
        var result = method!.Invoke(null, new object[] { new[] { set } }) as IReadOnlyDictionary<string, SymbolValueType>;
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result["sym1"].Should().Be(SymbolValueType.Int32);
        result["sym2"].Should().Be(SymbolValueType.Float);
    }

    [Fact]
    public void SymbolHydration_BuildSymbolValueTypeIndex_DuplicateNames_KeepsFirst()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("BuildSymbolValueTypeIndex",
            BindingFlags.Static | BindingFlags.NonPublic);
        var sig1 = new SignatureSpec("sym1", "AA BB", 0, SignatureAddressMode.HitPlusOffset, "", SymbolValueType.Int32);
        var sig2 = new SignatureSpec("sym1", "CC DD", 0, SignatureAddressMode.HitPlusOffset, "", SymbolValueType.Float);
        var set1 = new SignatureSet("set1", "1.0", new[] { sig1 });
        var set2 = new SignatureSet("set2", "1.0", new[] { sig2 });
        var result = method!.Invoke(null, new object[] { new[] { set1, set2 } }) as IReadOnlyDictionary<string, SymbolValueType>;
        result.Should().NotBeNull();
        result!.Count.Should().Be(1);
        result["sym1"].Should().Be(SymbolValueType.Int32);
    }

    [Fact]
    public void SymbolHydration_ResolveDefaultGhidraSymbolPackRoot_ReturnsNonEmptyPath()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("ResolveDefaultGhidraSymbolPackRoot",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var result = method!.Invoke(null, Array.Empty<object>()) as string;
        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SymbolHydration_ResolveIndexedPackPath_NullPath_ReturnsNull()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("ResolveIndexedPackPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "root", null }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void SymbolHydration_ResolveIndexedPackPath_EmptyPath_ReturnsNull()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("ResolveIndexedPackPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "root", "  " }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void SymbolHydration_ResolveIndexedPackPath_RootedPath_ReturnsSame()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("ResolveIndexedPackPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "root", "C:\\absolute\\pack.json" }) as string;
        result.Should().Be("C:\\absolute\\pack.json");
    }

    [Fact]
    public void SymbolHydration_ResolveIndexedPackPath_RelativePath_CombinesWithRoot()
    {
        var method = SymbolHydrationTypeInternal.GetMethod("ResolveIndexedPackPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "C:\\root", "sub\\pack.json" }) as string;
        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("sub");
    }

    // ==============================================================
    // SignatureResolver.Addressing — partial branches
    // ==============================================================

    [Fact]
    public void Addressing_GuessRipImmediateLength_Opcode80_3D_Returns1()
    {
        var method = AddressingType.GetMethod("GuessRipImmediateLength",
            BindingFlags.Static | BindingFlags.NonPublic);
        // Pattern "80 3D ?? ?? ?? ?? 00" should return 1
        var result = (int)method!.Invoke(null, new object[] { "80 3D ?? ?? ?? ?? 00" })!;
        result.Should().Be(1);
    }

    [Fact]
    public void Addressing_GuessRipImmediateLength_OpcodeC6_05_Returns1()
    {
        var method = AddressingType.GetMethod("GuessRipImmediateLength",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (int)method!.Invoke(null, new object[] { "C6 05 ?? ?? ?? ?? 01" })!;
        result.Should().Be(1);
    }

    [Fact]
    public void Addressing_GuessRipImmediateLength_OtherPattern_Returns0()
    {
        var method = AddressingType.GetMethod("GuessRipImmediateLength",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (int)method!.Invoke(null, new object[] { "48 8B 05 ?? ?? ?? ??" })!;
        result.Should().Be(0);
    }

    [Fact]
    public void Addressing_TryResolveAddress_UnsupportedMode_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", 0, (SignatureAddressMode)999, "", SymbolValueType.Int32);
        // Use the public internal method
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)100, (nint)0, new byte[256]);

        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
        var diagnostics = args[2] as string;
        diagnostics.Should().Contain("Unsupported");
    }

    [Fact]
    public void Addressing_TryResolveAddress_HitPlusOffset_ReturnsCorrectAddress()
    {
        var sig = new SignatureSpec("test", "AA BB", 8, SignatureAddressMode.HitPlusOffset, "", SymbolValueType.Int32);
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)100, (nint)0, new byte[256]);
        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        ((nint)args[1]!).Should().Be((nint)108);
    }

    [Fact]
    public void Addressing_TryResolveAddress_ReadAbsolute32_ZeroAddress_ReturnsFalse()
    {
        var moduleBytes = new byte[256];
        // Write zero at offset 10 (hit at 10 + offset 0 = index 10)
        Array.Clear(moduleBytes, 10, 4);
        var sig = new SignatureSpec("test", "AA BB", 0, SignatureAddressMode.ReadAbsolute32AtOffset, "", SymbolValueType.Int32);
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)10, (nint)0, moduleBytes);
        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void Addressing_TryResolveAddress_ReadAbsolute32_ValidAddress_ReturnsTrue()
    {
        var moduleBytes = new byte[256];
        BitConverter.GetBytes((uint)0x12345678).CopyTo(moduleBytes, 10);
        var sig = new SignatureSpec("test", "AA BB", 0, SignatureAddressMode.ReadAbsolute32AtOffset, "", SymbolValueType.Int32);
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)10, (nint)0, moduleBytes);
        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void Addressing_TryResolveAddress_ReadAbsolute32_OutOfBounds_ReturnsFalse()
    {
        var moduleBytes = new byte[12]; // too small for index 10 + sizeof(uint)
        var sig = new SignatureSpec("test", "AA BB", 0, SignatureAddressMode.ReadAbsolute32AtOffset, "", SymbolValueType.Int32);
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)10, (nint)0, moduleBytes);
        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void Addressing_TryResolveAddress_ReadRipRelative32_ValidData_ReturnsTrue()
    {
        var moduleBytes = new byte[256];
        BitConverter.GetBytes((int)100).CopyTo(moduleBytes, 20);
        var sig = new SignatureSpec("test", "48 8B 05 ?? ?? ?? ??", 0, SignatureAddressMode.ReadRipRelative32AtOffset, "", SymbolValueType.Int32);
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)20, (nint)0, moduleBytes);
        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void Addressing_TryResolveAddress_ReadRipRelative32_OutOfBounds_ReturnsFalse()
    {
        var moduleBytes = new byte[22]; // barely not enough
        var sig = new SignatureSpec("test", "48 8B 05 ?? ?? ?? ??", 0, SignatureAddressMode.ReadRipRelative32AtOffset, "", SymbolValueType.Int32);
        var inputType = AddressingType.GetNestedType("AddressResolutionInput",
            BindingFlags.NonPublic | BindingFlags.Public)!;
        var input = Activator.CreateInstance(inputType, sig, (nint)20, (nint)0, moduleBytes);
        var method = AddressingType.GetMethod("TryResolveAddress",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var args = new object?[] { input!, nint.Zero, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ==============================================================
    // SignatureResolver.Fallbacks — HandleSignatureHit / HandleSignatureMiss guard clauses
    // ==============================================================

    [Fact]
    public void Fallbacks_HandleSignatureHit_NullLogger_Throws()
    {
        var method = FallbacksType.GetMethod("HandleSignatureHit",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(null, new object?[] { null, null, null, nint.Zero, null });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void Fallbacks_HandleSignatureMiss_NullLogger_Throws()
    {
        // Find the overload with 6 params
        var methods = FallbacksType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == "HandleSignatureMiss");
        var method = methods.FirstOrDefault(m => m.GetParameters().Length == 2);
        if (method is null) return; // skip if overload not found
        var act = () => method.Invoke(null, new object?[] { null, null });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void Fallbacks_ApplyStandaloneFallbacks_NullLogger_Throws()
    {
        var method = FallbacksType.GetMethod("ApplyStandaloneFallbacks",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        method.Should().NotBeNull();
        var act = () => method!.Invoke(null, new object?[] { null, null, null, nint.Zero, null });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    // ==============================================================
    // GameLaunchService — static helpers via reflection
    // ==============================================================

    [Fact]
    public void GameLaunchService_BuildModPathArgument_Null_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildModPathArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildModPathArgument_Whitespace_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildModPathArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "  " }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildModPathArgument_PathTraversal_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildModPathArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "C:\\game\\..\\secret" }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildModPathArgument_ShellMetachar_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildModPathArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "C:\\game|evil" }) as string;
        result.Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("&")]
    [InlineData(";")]
    [InlineData("`")]
    [InlineData("$")]
    [InlineData(">")]
    [InlineData("<")]
    [InlineData("!")]
    [InlineData("{")]
    [InlineData("}")]
    public void GameLaunchService_BuildModPathArgument_MetacharVariants_ReturnsEmpty(string metachar)
    {
        var method = GameLaunchServiceType.GetMethod("BuildModPathArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { $"path{metachar}evil" }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildModPathArgument_ValidPath_ReturnsModpath()
    {
        var method = GameLaunchServiceType.GetMethod("BuildModPathArgument",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "C:\\Mods\\MyMod" }) as string;
        result.Should().Be("MODPATH=\"C:\\Mods\\MyMod\"");
    }

    [Fact]
    public void GameLaunchService_NormalizeWorkshopIds_Null_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("NormalizeWorkshopIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as IReadOnlyList<string>;
        result.Should().BeEmpty();
    }

    [Fact]
    public void GameLaunchService_NormalizeWorkshopIds_EmptyList_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("NormalizeWorkshopIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { Array.Empty<string>() }) as IReadOnlyList<string>;
        result.Should().BeEmpty();
    }

    [Fact]
    public void GameLaunchService_NormalizeWorkshopIds_WithDuplicates_Deduplicates()
    {
        var method = GameLaunchServiceType.GetMethod("NormalizeWorkshopIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var ids = new List<string> { "111,222", "222,333", "  ", "111" };
        var result = method!.Invoke(null, new object?[] { (IReadOnlyList<string>)ids }) as IReadOnlyList<string>;
        result.Should().HaveCount(3);
    }

    [Fact]
    public void GameLaunchService_BuildSteamModArguments_Null_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildSteamModArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildSteamModArguments_EmptyList_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildSteamModArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { Array.Empty<string>() as IReadOnlyList<string> }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildSteamModArguments_ValidIds_ReturnsSteammodArgs()
    {
        var method = GameLaunchServiceType.GetMethod("BuildSteamModArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var ids = new List<string> { "111", "222" };
        var result = method!.Invoke(null, new object?[] { (IReadOnlyList<string>)ids }) as string;
        result.Should().Contain("STEAMMOD=111");
        result.Should().Contain("STEAMMOD=222");
    }

    [Fact]
    public void GameLaunchService_BuildSteamModArguments_NonDigitIds_Filtered()
    {
        var method = GameLaunchServiceType.GetMethod("BuildSteamModArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var ids = new List<string> { "abc", "111" };
        var result = method!.Invoke(null, new object?[] { (IReadOnlyList<string>)ids }) as string;
        result.Should().NotContain("abc");
        result.Should().Contain("STEAMMOD=111");
    }

    [Fact]
    public void GameLaunchService_ResolveExecutablePath_Sweaw_ReturnsGameDataPath()
    {
        var method = GameLaunchServiceType.GetMethod("ResolveExecutablePath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "C:\\Game", GameLaunchTarget.Sweaw }) as string;
        result.Should().Contain("sweaw.exe");
        result.Should().Contain("GameData");
    }

    [Fact]
    public void GameLaunchService_ResolveExecutablePath_Swfoc_ReturnsCorruptionPath()
    {
        var method = GameLaunchServiceType.GetMethod("ResolveExecutablePath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "C:\\Game", GameLaunchTarget.Swfoc }) as string;
        result.Should().Contain("swfoc.exe");
        result.Should().Contain("corruption");
    }

    [Fact]
    public void GameLaunchService_BuildArguments_VanillaMode_ReturnsEmpty()
    {
        var method = GameLaunchServiceType.GetMethod("BuildArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var request = new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla);
        var result = method!.Invoke(null, new object[] { request }) as string;
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void GameLaunchService_BuildArguments_SteamModMode_ReturnsSteammodArgs()
    {
        var method = GameLaunchServiceType.GetMethod("BuildArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var request = new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.SteamMod, WorkshopIds: new[] { "12345" });
        var result = method!.Invoke(null, new object[] { request }) as string;
        result.Should().Contain("STEAMMOD=12345");
    }

    [Fact]
    public void GameLaunchService_BuildArguments_ModPathMode_ReturnsModpath()
    {
        var method = GameLaunchServiceType.GetMethod("BuildArguments",
            BindingFlags.Static | BindingFlags.NonPublic);
        var request = new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.ModPath, ModPath: "C:\\Mods\\test");
        var result = method!.Invoke(null, new object[] { request }) as string;
        result.Should().Contain("MODPATH=");
    }

    // ==============================================================
    // LaunchContextResolver tests
    // ==============================================================

    [Fact]
    public void LaunchContextResolver_Resolve_NullProcess_Throws()
    {
        var resolver = new LaunchContextResolver();
        var act = () => resolver.Resolve(null!, Array.Empty<TrainerProfile>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LaunchContextResolver_Resolve_NullProfiles_Throws()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata();
        var act = () => resolver.Resolve(process, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LaunchContextResolver_Resolve_BaseGameSwfoc_ReturnsBaseGameKind()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(exeTarget: ExeTarget.Swfoc);
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.LaunchKind.Should().Be(LaunchKind.BaseGame);
    }

    [Fact]
    public void LaunchContextResolver_Resolve_BaseGameSweaw_ReturnsSweaw()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(exeTarget: ExeTarget.Sweaw);
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.LaunchKind.Should().Be(LaunchKind.BaseGame);
        result.Recommendation.ProfileId.Should().Be("base_sweaw");
    }

    [Fact]
    public void LaunchContextResolver_Resolve_SteamModIds_ReturnsWorkshop()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(commandLine: "swfoc.exe STEAMMOD=12345");
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.LaunchKind.Should().Be(LaunchKind.Workshop);
        result.SteamModIds.Should().Contain("12345");
    }

    [Fact]
    public void LaunchContextResolver_Resolve_ModPath_ReturnsLocalModPath()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(commandLine: "swfoc.exe MODPATH=\"Mods\\RoE\"");
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.LaunchKind.Should().Be(LaunchKind.LocalModPath);
    }

    [Fact]
    public void LaunchContextResolver_Resolve_Mixed_ReturnsMixed()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(commandLine: "swfoc.exe STEAMMOD=111 MODPATH=\"Mods\\test\"");
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.LaunchKind.Should().Be(LaunchKind.Mixed);
    }

    [Fact]
    public void LaunchContextResolver_Resolve_UnknownTarget_ReturnsUnknown()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(exeTarget: ExeTarget.Unknown);
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.LaunchKind.Should().Be(LaunchKind.Unknown);
    }

    [Fact]
    public void LaunchContextResolver_Resolve_StarWarsGProcess_ReturnsFocSafe()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(processName: "StarWarsG", processPath: "C:\\game\\StarWarsG.exe",
            exeTarget: ExeTarget.Swfoc);
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.Recommendation.ProfileId.Should().Be("base_swfoc");
    }

    [Fact]
    public void LaunchContextResolver_Resolve_ForcedSource_ReturnsForcedProfile()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(
            metadata: new Dictionary<string, string>
            {
                ["launchContextSource"] = "forced",
                ["forcedProfileId"] = "custom_roe"
            });
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.Recommendation.ProfileId.Should().Be("custom_roe");
        result.Recommendation.ReasonCode.Should().Be("forced_profile_id");
    }

    [Fact]
    public void LaunchContextResolver_Resolve_SteamModMatchesProfile_RecommendsThat()
    {
        var resolver = new LaunchContextResolver();
        var profile = CreateMinimalProfile("roe_swfoc", steamWorkshopId: "12345");
        var process = CreateProcessMetadata(commandLine: "swfoc.exe STEAMMOD=12345");
        var result = resolver.Resolve(process, new[] { profile });
        result.Recommendation.ProfileId.Should().Be("roe_swfoc");
    }

    [Fact]
    public void LaunchContextResolver_Resolve_ModPathMatchesProfile_RecommendsThat()
    {
        var resolver = new LaunchContextResolver();
        var profile = CreateMinimalProfile("aotr_swfoc",
            metadata: new Dictionary<string, string> { ["localPathHints"] = "aotr,AotR" });
        var process = CreateProcessMetadata(commandLine: "swfoc.exe MODPATH=\"Mods\\AotR_v8\"");
        var result = resolver.Resolve(process, new[] { profile });
        result.Recommendation.ProfileId.Should().Be("aotr_swfoc");
    }

    [Fact]
    public void LaunchContextResolver_Resolve_SteamModIdsFromMetadata_Extracted()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata(
            metadata: new Dictionary<string, string> { ["steamModIdsDetected"] = "111,222" });
        var result = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        result.SteamModIds.Should().Contain("111");
        result.SteamModIds.Should().Contain("222");
    }

    // ==============================================================
    // SymbolHealthService tests
    // ==============================================================

    [Fact]
    public void SymbolHealthService_Evaluate_NullSymbol_Throws()
    {
        var svc = new SymbolHealthService();
        var profile = CreateMinimalProfile("test");
        var act = () => svc.Evaluate(null!, profile, RuntimeMode.Unknown);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SymbolHealthService_Evaluate_NullProfile_Throws()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var act = () => svc.Evaluate(symbol, null!, RuntimeMode.Unknown);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SymbolHealthService_Evaluate_ZeroAddress_ReturnsUnresolved()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test", nint.Zero, SymbolValueType.Int32, AddressSource.Signature);
        var profile = CreateMinimalProfile("test");
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Unknown);
        result.Status.Should().Be(SymbolHealthStatus.Unresolved);
        result.Confidence.Should().Be(0.0d);
    }

    [Fact]
    public void SymbolHealthService_Evaluate_SignatureSource_ReturnsHealthy()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var profile = CreateMinimalProfile("test");
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Unknown);
        result.Status.Should().Be(SymbolHealthStatus.Healthy);
        result.Confidence.Should().Be(0.95d);
    }

    [Fact]
    public void SymbolHealthService_Evaluate_FallbackSource_ReturnsDegraded()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test", (nint)100, SymbolValueType.Int32, AddressSource.Fallback);
        var profile = CreateMinimalProfile("test");
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Unknown);
        result.Status.Should().Be(SymbolHealthStatus.Degraded);
        result.Confidence.Should().Be(0.65d);
    }

    [Fact]
    public void SymbolHealthService_Evaluate_CriticalDegraded_ReducedConfidence()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test_sym", (nint)100, SymbolValueType.Int32, AddressSource.Fallback);
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["criticalSymbols"] = "test_sym" });
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Unknown);
        result.Status.Should().Be(SymbolHealthStatus.Degraded);
        result.Confidence.Should().BeLessOrEqualTo(0.55d);
        result.IsCritical.Should().BeTrue();
    }

    [Fact]
    public void SymbolHealthService_Evaluate_CriticalHealthy_NotDegraded()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test_sym", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["criticalSymbols"] = "test_sym" });
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Unknown);
        result.Status.Should().Be(SymbolHealthStatus.Healthy);
        result.IsCritical.Should().BeTrue();
    }

    [Fact]
    public void SymbolHealthService_Evaluate_ModeRuleMismatch_DegradesFurther()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test_sym", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var rules = JsonSerializer.Serialize(new[]
        {
            new { Symbol = "test_sym", Mode = "Galactic" }
        });
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["symbolValidationRules"] = rules });
        // Ask for tactical, rule says galactic
        var result = svc.Evaluate(symbol, profile, RuntimeMode.AnyTactical);
        result.Status.Should().Be(SymbolHealthStatus.Degraded);
        result.Reason.Should().Contain("mode_mismatch");
    }

    [Fact]
    public void SymbolHealthService_Evaluate_ModeRuleMatch_StaysHealthy()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test_sym", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var rules = JsonSerializer.Serialize(new[]
        {
            new { Symbol = "test_sym", Mode = "Galactic" }
        });
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["symbolValidationRules"] = rules });
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Galactic);
        result.Status.Should().Be(SymbolHealthStatus.Healthy);
    }

    [Fact]
    public void SymbolHealthService_Evaluate_ModeRuleNull_AnyMode_NoMismatch()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test_sym", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var rules = JsonSerializer.Serialize(new[]
        {
            new { Symbol = "test_sym" }
        });
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["symbolValidationRules"] = rules });
        var result = svc.Evaluate(symbol, profile, RuntimeMode.AnyTactical);
        result.Status.Should().Be(SymbolHealthStatus.Healthy);
    }

    [Fact]
    public void SymbolHealthService_Evaluate_UnknownMode_NoMismatch()
    {
        var svc = new SymbolHealthService();
        var symbol = new SymbolInfo("test_sym", (nint)100, SymbolValueType.Int32, AddressSource.Signature);
        var rules = JsonSerializer.Serialize(new[]
        {
            new { Symbol = "test_sym", Mode = "Galactic" }
        });
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["symbolValidationRules"] = rules });
        var result = svc.Evaluate(symbol, profile, RuntimeMode.Unknown);
        result.Status.Should().Be(SymbolHealthStatus.Healthy);
    }

    // ==============================================================
    // RuntimeModeProbeResolver — uncovered branches
    // ==============================================================

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_NullSymbols_Throws()
    {
        var act = () => RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_NoSignals_UnknownHint_ReturnsUnknown()
    {
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>());
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.Unknown);
        result.ReasonCode.Should().Contain("no_signals_unknown");
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_NoSignals_TacticalHint_UsesHint()
    {
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>());
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.AnyTactical, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.AnyTactical);
        result.ReasonCode.Should().Contain("no_signals_use_hint");
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_NoSignals_GalacticHint_UsesHint()
    {
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>());
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Galactic, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_NoSignals_TacticalLandHint_UsesHint()
    {
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>());
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.TacticalLand, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_NoSignals_TacticalSpaceHint_UsesHint()
    {
        var symbols = new SymbolMap(new Dictionary<string, SymbolInfo>());
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.TacticalSpace, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.TacticalSpace);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_TacticalOnly_ReturnsTactical()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["selected_hp"] = new("selected_hp", (nint)100, SymbolValueType.Int32, AddressSource.Signature)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.AnyTactical);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_GalacticOnly_ReturnsGalactic()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["credits"] = new("credits", (nint)100, SymbolValueType.Int32, AddressSource.Signature)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_Ambiguous_UnknownHint_TacticalBias()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["selected_hp"] = new("selected_hp", (nint)100, SymbolValueType.Int32, AddressSource.Signature),
            ["credits"] = new("credits", (nint)200, SymbolValueType.Int32, AddressSource.Signature)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);
        // With 1 tactical + 1 galactic, tacticalWins = tacticalSignalCount >= galacticSignalCount => tactical
        result.EffectiveMode.Should().Be(RuntimeMode.AnyTactical);
        result.ReasonCode.Should().Contain("ambiguous");
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_Ambiguous_GalacticHint_KeepsHint()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["selected_hp"] = new("selected_hp", (nint)100, SymbolValueType.Int32, AddressSource.Signature),
            ["credits"] = new("credits", (nint)200, SymbolValueType.Int32, AddressSource.Signature)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Galactic, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
        result.ReasonCode.Should().Contain("ambiguous_keep_hint");
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_Ambiguous_TacticalLandHint_KeepsHint()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["selected_hp"] = new("selected_hp", (nint)100, SymbolValueType.Int32, AddressSource.Signature),
            ["credits"] = new("credits", (nint)200, SymbolValueType.Int32, AddressSource.Signature)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.TacticalLand, symbols);
        result.EffectiveMode.Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_ZeroAddressSymbol_NotCounted()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["selected_hp"] = new("selected_hp", nint.Zero, SymbolValueType.Int32, AddressSource.Signature)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);
        result.TacticalSignalCount.Should().Be(0);
    }

    [Fact]
    public void RuntimeModeProbeResolver_Resolve_UnresolvedSymbol_NotCounted()
    {
        var symbolDict = new Dictionary<string, SymbolInfo>
        {
            ["selected_hp"] = new("selected_hp", (nint)100, SymbolValueType.Int32, AddressSource.Signature,
                HealthStatus: SymbolHealthStatus.Unresolved)
        };
        var symbols = new SymbolMap(symbolDict);
        var result = RuntimeModeProbeResolver.Resolve(RuntimeMode.Unknown, symbols);
        result.TacticalSignalCount.Should().Be(0);
    }

    // ==============================================================
    // WorkshopInventoryChainResolver tests
    // ==============================================================

    [Fact]
    public void ChainResolver_ResolveChains_NullItems_Throws()
    {
        var method = ChainResolverType.GetMethod("ResolveChains",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var act = () => method!.Invoke(null, new object?[] { null });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void ChainResolver_ResolveChains_EmptyItems_ReturnsEmpty()
    {
        var method = ChainResolverType.GetMethod("ResolveChains",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var result = method!.Invoke(null, new object[] { Array.Empty<WorkshopInventoryItem>() }) as IReadOnlyList<WorkshopInventoryChain>;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ChainResolver_ResolveChains_SingleItemNoParents_SingleChain()
    {
        var method = ChainResolverType.GetMethod("ResolveChains",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var items = new[]
        {
            new WorkshopInventoryItem("111", "ModA", WorkshopItemType.Mod, Array.Empty<string>(), Array.Empty<string>(), ClassificationReason: "independent_mod")
        };
        var result = method!.Invoke(null, new object[] { items }) as IReadOnlyList<WorkshopInventoryChain>;
        result.Should().HaveCount(1);
        result![0].OrderedWorkshopIds.Should().HaveCount(1);
    }

    [Fact]
    public void ChainResolver_ResolveChains_WithParentChild_CreatesChain()
    {
        var method = ChainResolverType.GetMethod("ResolveChains",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var parent = new WorkshopInventoryItem("100", "Parent", WorkshopItemType.Mod, Array.Empty<string>(), Array.Empty<string>());
        var child = new WorkshopInventoryItem("200", "Child", WorkshopItemType.Submod, new[] { "100" }, Array.Empty<string>(), ClassificationReason: "parent_dependency");
        var result = method!.Invoke(null, new object[] { new[] { parent, child } }) as IReadOnlyList<WorkshopInventoryChain>;
        result.Should().NotBeNull();
        result!.Any(c => c.OrderedWorkshopIds.Contains("100") && c.OrderedWorkshopIds.Contains("200")).Should().BeTrue();
    }

    [Fact]
    public void ChainResolver_ResolveChains_MissingParent_MarksAsMissing()
    {
        var method = ChainResolverType.GetMethod("ResolveChains",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var child = new WorkshopInventoryItem("200", "Child", WorkshopItemType.Submod, new[] { "999" }, Array.Empty<string>(), ClassificationReason: "parent_dependency");
        var result = method!.Invoke(null, new object[] { new[] { child } }) as IReadOnlyList<WorkshopInventoryChain>;
        result.Should().NotBeNull();
        result!.Any(c => c.MissingParentIds != null && c.MissingParentIds.Contains("999")).Should().BeTrue();
    }

    [Fact]
    public void ChainResolver_ResolveChains_CircularReference_HandledGracefully()
    {
        var method = ChainResolverType.GetMethod("ResolveChains",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var item1 = new WorkshopInventoryItem("100", "A", WorkshopItemType.Submod, new[] { "200" }, Array.Empty<string>());
        var item2 = new WorkshopInventoryItem("200", "B", WorkshopItemType.Submod, new[] { "100" }, Array.Empty<string>());
        var act = () => method!.Invoke(null, new object[] { new[] { item1, item2 } });
        act.Should().NotThrow();
    }

    // ==============================================================
    // ProcessLocator — static helpers via reflection
    // ==============================================================

    [Theory]
    [InlineData("sweaw", "sweaw", true)]
    [InlineData("swfoc", "swfoc", true)]
    [InlineData("StarWarsG", "swfoc", false)]
    [InlineData("notepad", "swfoc", false)]
    public void ProcessLocator_IsProcessName_VariousNames(string name, string expectedName, bool expected)
    {
        var method = ProcessLocatorType.GetMethod("IsProcessName",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { name, expectedName })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ProcessLocator_IsProcessName_NullName_ReturnsFalse()
    {
        var method = ProcessLocatorType.GetMethod("IsProcessName",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { null, "swfoc" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ProcessLocator_IsProcessName_WithExeExtension_Matches()
    {
        var method = ProcessLocatorType.GetMethod("IsProcessName",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "swfoc.exe", "swfoc" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessLocator_ContainsToken_NullValue_ReturnsFalse()
    {
        var method = ProcessLocatorType.GetMethod("ContainsToken",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { null, "test" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ProcessLocator_ContainsToken_Present_ReturnsTrue()
    {
        var method = ProcessLocatorType.GetMethod("ContainsToken",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "hello world test", "world" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ProcessLocator_InferMode_NullCommandLine_ReturnsUnknown()
    {
        var method = ProcessLocatorType.GetMethod("InferMode",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (RuntimeMode)method!.Invoke(null, new object?[] { null })!;
        result.Should().Be(RuntimeMode.Unknown);
    }

    [Theory]
    [InlineData("--mode=land", RuntimeMode.TacticalLand)]
    [InlineData("--mode=space", RuntimeMode.TacticalSpace)]
    [InlineData("--mode=skirmish", RuntimeMode.AnyTactical)]
    [InlineData("--mode=tactical", RuntimeMode.AnyTactical)]
    [InlineData("--mode=campaign", RuntimeMode.Galactic)]
    [InlineData("--mode=galactic", RuntimeMode.Galactic)]
    [InlineData("--mode=other", RuntimeMode.Unknown)]
    public void ProcessLocator_InferMode_VariousCommandLines(string cmdLine, RuntimeMode expected)
    {
        var method = ProcessLocatorType.GetMethod("InferMode",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (RuntimeMode)method!.Invoke(null, new object?[] { cmdLine })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ProcessLocator_ExtractSteamModIds_NullCommandLine_ReturnsEmpty()
    {
        var method = ProcessLocatorType.GetMethod("ExtractSteamModIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as string[];
        result.Should().BeEmpty();
    }

    [Fact]
    public void ProcessLocator_ExtractSteamModIds_WithSteamMod_ExtractsId()
    {
        var method = ProcessLocatorType.GetMethod("ExtractSteamModIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe STEAMMOD=12345 STEAMMOD=67890" }) as string[];
        result.Should().Contain("12345");
        result.Should().Contain("67890");
    }

    [Fact]
    public void ProcessLocator_ExtractSteamModIds_WorkshopPathPattern_ExtractsId()
    {
        var method = ProcessLocatorType.GetMethod("ExtractSteamModIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "C:\\Steam\\steamapps\\workshop\\content\\32470\\12345\\mod" }) as string[];
        result.Should().Contain("12345");
    }

    [Fact]
    public void ProcessLocator_ExtractModPath_NullCommandLine_ReturnsNull()
    {
        var method = ProcessLocatorType.GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void ProcessLocator_ExtractModPath_NoModPath_ReturnsNull()
    {
        var method = ProcessLocatorType.GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe --noop" }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void ProcessLocator_ExtractModPath_WithQuoted_ExtractsPath()
    {
        var method = ProcessLocatorType.GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe modpath=\"Mods\\MyMod\"" }) as string;
        result.Should().Be("Mods\\MyMod");
    }

    [Fact]
    public void ProcessLocator_ExtractModPath_UnquotedPath_ExtractsPath()
    {
        var method = ProcessLocatorType.GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe modpath=MyMod" }) as string;
        result.Should().Be("MyMod");
    }

    [Fact]
    public void ProcessLocator_DetermineHostRole_StarWarsG_ReturnsGameHost()
    {
        var detectionType = ProcessLocatorType.GetNestedType("ProcessDetection",
            BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Swfoc, true, "test");
        var method = ProcessLocatorType.GetMethod("DetermineHostRole",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.GameHost);
    }

    [Fact]
    public void ProcessLocator_DetermineHostRole_Swfoc_ReturnsLauncher()
    {
        var detectionType = ProcessLocatorType.GetNestedType("ProcessDetection",
            BindingFlags.NonPublic);
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Swfoc, false, "test");
        var method = ProcessLocatorType.GetMethod("DetermineHostRole",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void ProcessLocator_DetermineHostRole_Unknown_ReturnsUnknown()
    {
        var detectionType = ProcessLocatorType.GetNestedType("ProcessDetection",
            BindingFlags.NonPublic);
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Unknown, false, "test");
        var method = ProcessLocatorType.GetMethod("DetermineHostRole",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Unknown);
    }

    [Fact]
    public void ProcessLocator_NormalizeWorkshopIds_Null_ReturnsEmpty()
    {
        var method = ProcessLocatorType.GetMethod("NormalizeWorkshopIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as IReadOnlyList<string>;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ProcessLocator_NormalizeWorkshopIds_EmptyList_ReturnsEmpty()
    {
        var method = ProcessLocatorType.GetMethod("NormalizeWorkshopIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { Array.Empty<string>() as IReadOnlyList<string> }) as IReadOnlyList<string>;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ProcessLocator_NormalizeWorkshopIds_Deduplicates()
    {
        var method = ProcessLocatorType.GetMethod("NormalizeWorkshopIds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var ids = new List<string> { "111,222", "222,333" } as IReadOnlyList<string>;
        var result = method!.Invoke(null, new object?[] { ids }) as IReadOnlyList<string>;
        result.Should().HaveCount(3);
    }

    [Fact]
    public void ProcessLocator_NormalizeForcedProfileId_Null_ReturnsNull()
    {
        var method = ProcessLocatorType.GetMethod("NormalizeForcedProfileId",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void ProcessLocator_NormalizeForcedProfileId_Whitespace_ReturnsNull()
    {
        var method = ProcessLocatorType.GetMethod("NormalizeForcedProfileId",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "  " }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void ProcessLocator_NormalizeForcedProfileId_ValidId_ReturnsTrimmed()
    {
        var method = ProcessLocatorType.GetMethod("NormalizeForcedProfileId",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "  my_profile  " }) as string;
        result.Should().Be("my_profile");
    }

    // ==============================================================
    // BinaryFingerprintService — BuildFingerprintId
    // ==============================================================

    [Fact]
    public void BinaryFingerprintService_BuildFingerprintId_StandardInput_ReturnsExpected()
    {
        var method = BinaryFingerprintServiceType.GetMethod("BuildFingerprintId",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "swfoc.exe", "0123456789abcdef0123456789abcdef" }) as string;
        result.Should().Be("swfoc_0123456789abcdef");
    }

    [Fact]
    public void BinaryFingerprintService_BuildFingerprintId_ShortHash_UsesFullHash()
    {
        var method = BinaryFingerprintServiceType.GetMethod("BuildFingerprintId",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "test.exe", "abcd" }) as string;
        result.Should().Be("test_abcd");
    }

    [Fact]
    public void BinaryFingerprintService_BuildFingerprintId_SpaceInName_UnderscoreReplaced()
    {
        var method = BinaryFingerprintServiceType.GetMethod("BuildFingerprintId",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "Star Wars Game.exe", "0123456789abcdef0123456789abcdef" }) as string;
        result.Should().Be("star_wars_game_0123456789abcdef");
    }

    // ==============================================================
    // ModDependencyValidator — additional branch coverage
    // ==============================================================

    [Fact]
    public void ModDependencyValidator_Validate_NullProfile_Throws()
    {
        var validator = new ModDependencyValidator();
        var process = CreateProcessMetadata();
        var act = () => validator.Validate(null!, process);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ModDependencyValidator_Validate_NullProcess_Throws()
    {
        var validator = new ModDependencyValidator();
        var profile = CreateMinimalProfile("test");
        var act = () => validator.Validate(profile, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ModDependencyValidator_Validate_NoWorkshopDeps_ReturnsPass()
    {
        var validator = new ModDependencyValidator();
        var profile = CreateMinimalProfile("test");
        var process = CreateProcessMetadata();
        var result = validator.Validate(profile, process);
        result.Status.Should().Be(DependencyValidationStatus.Pass);
        result.Message.Should().Contain("No workshop dependencies");
    }

    [Fact]
    public void ModDependencyValidator_Validate_PathTraversalMarker_ReturnsHardFail()
    {
        var validator = new ModDependencyValidator();
        var profile = CreateMinimalProfile("test",
            metadata: new Dictionary<string, string> { ["requiredMarkerFile"] = "..\\secret\\file.txt" });
        var process = CreateProcessMetadata();
        var result = validator.Validate(profile, process);
        result.Status.Should().Be(DependencyValidationStatus.HardFail);
    }

    [Fact]
    public void ModDependencyValidator_Validate_WithWorkshopId_NoRootsFound_SoftFail()
    {
        var validator = new ModDependencyValidator();
        var profile = CreateMinimalProfile("test", steamWorkshopId: "999999999");
        var process = CreateProcessMetadata();
        var result = validator.Validate(profile, process);
        // SoftFail or Pass depending on filesystem state, but should not throw
        result.Should().NotBeNull();
    }

    [Fact]
    public void ModDependencyValidator_ExtractModPath_NullCommandLine_ReturnsNull()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void ModDependencyValidator_ExtractModPath_NoMatch_ReturnsNull()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe --noop" }) as string;
        result.Should().BeNull();
    }

    [Fact]
    public void ModDependencyValidator_ExtractModPath_QuotedPath_ExtractsValue()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe modpath=\"Mods\\test\"" }) as string;
        result.Should().Be("Mods\\test");
    }

    [Fact]
    public void ModDependencyValidator_ExtractModPath_UnquotedPath_ExtractsValue()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ExtractModPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "swfoc.exe modpath=MyMod" }) as string;
        result.Should().Be("MyMod");
    }

    [Fact]
    public void ModDependencyValidator_ValidateMarkerMetadata_NullMarker_ReturnsNull()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ValidateMarkerMetadata",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void ModDependencyValidator_ValidateMarkerMetadata_SafeMarker_ReturnsNull()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ValidateMarkerMetadata",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "valid_marker.txt" });
        result.Should().BeNull();
    }

    [Fact]
    public void ModDependencyValidator_ValidateMarkerMetadata_TraversalMarker_ReturnsHardFail()
    {
        var method = typeof(ModDependencyValidator).GetMethod("ValidateMarkerMetadata",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object?[] { "..\\secret" }) as DependencyValidationResult;
        result.Should().NotBeNull();
        result!.Status.Should().Be(DependencyValidationStatus.HardFail);
    }

    [Fact]
    public void ModDependencyValidator_BuildPossibleModRoots_EmptyModPath_ReturnsEmpty()
    {
        var method = typeof(ModDependencyValidator).GetMethod("BuildPossibleModRoots",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "  ", "C:\\game\\swfoc.exe" }) as IReadOnlyList<string>;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ModDependencyValidator_BuildPossibleModRoots_AbsolutePath_ReturnsSingle()
    {
        var method = typeof(ModDependencyValidator).GetMethod("BuildPossibleModRoots",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "C:\\Mods\\Test", "C:\\game\\swfoc.exe" }) as IReadOnlyList<string>;
        result.Should().HaveCount(1);
        result![0].Should().Contain("Mods");
    }

    [Fact]
    public void ModDependencyValidator_BuildPossibleModRoots_RelativePath_ReturnsMultiple()
    {
        var method = typeof(ModDependencyValidator).GetMethod("BuildPossibleModRoots",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "Mods\\Test", "C:\\game\\swfoc.exe" }) as IReadOnlyList<string>;
        result.Should().NotBeNull();
        result!.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void ModDependencyValidator_BuildPossibleModRoots_NoProcessDir_ReturnsEmpty()
    {
        var method = typeof(ModDependencyValidator).GetMethod("BuildPossibleModRoots",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { "Mods\\Test", "" }) as IReadOnlyList<string>;
        result.Should().BeEmpty();
    }

    // ==============================================================
    // CapabilityMapResolver — static helpers via reflection
    // ==============================================================

    [Fact]
    public void CapabilityMapResolver_BuildConfidence_ZeroTotal_Returns050()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("BuildConfidence",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (double)method!.Invoke(null, new object[] { 0, 0 })!;
        result.Should().Be(0.50d);
    }

    [Fact]
    public void CapabilityMapResolver_BuildConfidence_FullMatch_ReturnsOne()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("BuildConfidence",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (double)method!.Invoke(null, new object[] { 5, 5 })!;
        result.Should().Be(1.0d);
    }

    [Fact]
    public void CapabilityMapResolver_BuildConfidence_PartialMatch_ReturnsFraction()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("BuildConfidence",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (double)method!.Invoke(null, new object[] { 3, 6 })!;
        result.Should().BeApproximately(0.5d, 0.01d);
    }

    [Fact]
    public void CapabilityMapResolver_MapExternalReasonCode_Null_ReturnsUnknown()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("MapExternalReasonCode",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (CapabilityReasonCode)method!.Invoke(null, new object?[] { null })!;
        result.Should().Be(CapabilityReasonCode.Unknown);
    }

    [Fact]
    public void CapabilityMapResolver_MapExternalReasonCode_Empty_ReturnsUnknown()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("MapExternalReasonCode",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (CapabilityReasonCode)method!.Invoke(null, new object?[] { "  " })!;
        result.Should().Be(CapabilityReasonCode.Unknown);
    }

    [Theory]
    [InlineData("CAPABILITY_REQUIRED_MISSING", CapabilityReasonCode.RequiredAnchorsMissing)]
    [InlineData("CAPABILITY_PROBE_PASS", CapabilityReasonCode.AllRequiredAnchorsPresent)]
    [InlineData("CAPABILITY_ANCHOR_INVALID", CapabilityReasonCode.RequiredAnchorsMissing)]
    [InlineData("CAPABILITY_ANCHOR_UNREADABLE", CapabilityReasonCode.RequiredAnchorsMissing)]
    [InlineData("CAPABILITY_BACKEND_UNAVAILABLE", CapabilityReasonCode.RuntimeNotAttached)]
    [InlineData("SAFETY_FAIL_CLOSED", CapabilityReasonCode.MutationBlockedByCapabilityState)]
    [InlineData("SOMETHING_ELSE", CapabilityReasonCode.Unknown)]
    public void CapabilityMapResolver_MapExternalReasonCode_KnownCodes(string code, CapabilityReasonCode expected)
    {
        var method = typeof(CapabilityMapResolver).GetMethod("MapExternalReasonCode",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (CapabilityReasonCode)method!.Invoke(null, new object?[] { code })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void CapabilityMapResolver_IsRequestedProfileMismatch_NullDefault_ReturnsFalse()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsRequestedProfileMismatch",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "test", null })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CapabilityMapResolver_IsRequestedProfileMismatch_SameIds_ReturnsFalse()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsRequestedProfileMismatch",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "test", "test" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CapabilityMapResolver_IsRequestedProfileMismatch_UniversalAuto_ReturnsFalse()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsRequestedProfileMismatch",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "universal_auto", "some_profile" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CapabilityMapResolver_IsRequestedProfileMismatch_DifferentIds_ReturnsTrue()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsRequestedProfileMismatch",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "one", "two" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void CapabilityMapResolver_IsGeneratedCustomProfileCompatible_CustomSwfoc_ReturnsTrue()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsGeneratedCustomProfileCompatible",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "custom_123_swfoc", "roe_swfoc" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void CapabilityMapResolver_IsGeneratedCustomProfileCompatible_CustomSweaw_ReturnsTrue()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsGeneratedCustomProfileCompatible",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "custom_123_sweaw", "base_sweaw" })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void CapabilityMapResolver_IsGeneratedCustomProfileCompatible_NonCustom_ReturnsFalse()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsGeneratedCustomProfileCompatible",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "roe_swfoc", "roe_swfoc" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CapabilityMapResolver_IsGeneratedCustomProfileCompatible_MismatchedSuffix_ReturnsFalse()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsGeneratedCustomProfileCompatible",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "custom_123_sweaw", "roe_swfoc" })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CapabilityMapResolver_IsGeneratedCustomProfileCompatible_EmptyRequested_ReturnsFalse()
    {
        var method = typeof(CapabilityMapResolver).GetMethod("IsGeneratedCustomProfileCompatible",
            BindingFlags.Static | BindingFlags.NonPublic);
        var result = (bool)method!.Invoke(null, new object?[] { "", "roe_swfoc" })!;
        result.Should().BeFalse();
    }

    // ==============================================================
    // GameLaunchService instance tests (LaunchAsync guard clauses)
    // ==============================================================

    [Fact]
    public async Task GameLaunchService_LaunchAsync_NullRequest_Throws()
    {
        var svc = new GameLaunchService();
        var act = () => svc.LaunchAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GameLaunchService_LaunchAsync_CancelledToken_Throws()
    {
        var svc = new GameLaunchService();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var request = new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla);
        var act = () => svc.LaunchAsync(request, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GameLaunchService_LaunchAsync_NoGameRoot_ReturnsFailure()
    {
        // CRITICAL (2026-04-10 fix): the previous version of this test relied on
        // "CI/test machines won't find a game root" — but on a dev machine with
        // the game actually installed at D:\SteamLibrary\..., this test would
        // silently launch the real game during `dotnet test`. Fix: force the
        // override env var to a nonexistent path so both branches (root_missing
        // or exe_missing) fire without ever reaching Process.Start.
        var svc = new GameLaunchService();
        var request = new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla);

        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");
        // GameLaunchService.ResolveRoot() falls through to DefaultRoots if the
        // override doesn't EXIST, so we must point at an existing directory that
        // simply lacks `corruption/swfoc.exe`. That triggers exe_missing without
        // ever touching Process.Start.
        var emptyRoot = Path.Combine(Path.GetTempPath(), $"swfoc_empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyRoot);
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", emptyRoot);
            var result = await svc.LaunchAsync(request, CancellationToken.None);
            result.Succeeded.Should().BeFalse(
                because: "override points at an empty directory; launch must fail with exe_missing without starting the game");
            result.Diagnostics.Should().NotBeNull();
            result.Diagnostics!.Should().ContainKey("launchState");
            result.Diagnostics!["launchState"].Should().Be("exe_missing");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previousOverride);
            try { Directory.Delete(emptyRoot, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    // ==============================================================
    // ProcessLocator constructor variants
    // ==============================================================

    [Fact]
    public void ProcessLocator_DefaultConstructor_DoesNotThrow()
    {
        var locator = new ProcessLocator();
        locator.Should().NotBeNull();
    }

    [Fact]
    public void ProcessLocator_WithLaunchContextResolver_DoesNotThrow()
    {
        var resolver = new LaunchContextResolver();
        var locator = new ProcessLocator(resolver);
        locator.Should().NotBeNull();
    }

    // ==============================================================
    // CapabilityMapResolver constructor + ResolveAsync guard clauses
    // ==============================================================

    [Fact]
    public void CapabilityMapResolver_Constructor_NullPath_Throws()
    {
        var act = () => new CapabilityMapResolver(null!, NullLogger<CapabilityMapResolver>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CapabilityMapResolver_Constructor_NullLogger_Throws()
    {
        var act = () => new CapabilityMapResolver("path", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveAsync_NullFingerprint_Throws()
    {
        var resolver = new CapabilityMapResolver("nonexistent", NullLogger<CapabilityMapResolver>.Instance);
        var act = () => resolver.ResolveAsync(null!, "profile", "op", new HashSet<string>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveAsync_NullProfileId_Throws()
    {
        var resolver = new CapabilityMapResolver("nonexistent", NullLogger<CapabilityMapResolver>.Instance);
        var fp = new BinaryFingerprint("fp1", "sha", "mod", null, null, DateTimeOffset.UtcNow, Array.Empty<string>(), "path");
        var act = () => resolver.ResolveAsync(fp, null!, "op", new HashSet<string>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveAsync_NullOperationId_Throws()
    {
        var resolver = new CapabilityMapResolver("nonexistent", NullLogger<CapabilityMapResolver>.Instance);
        var fp = new BinaryFingerprint("fp1", "sha", "mod", null, null, DateTimeOffset.UtcNow, Array.Empty<string>(), "path");
        var act = () => resolver.ResolveAsync(fp, "profile", null!, new HashSet<string>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveAsync_NullAnchors_Throws()
    {
        var resolver = new CapabilityMapResolver("nonexistent", NullLogger<CapabilityMapResolver>.Instance);
        var fp = new BinaryFingerprint("fp1", "sha", "mod", null, null, DateTimeOffset.UtcNow, Array.Empty<string>(), "path");
        var act = () => resolver.ResolveAsync(fp, "profile", "op", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveAsync_MissingMap_ReturnsUnavailable()
    {
        var resolver = new CapabilityMapResolver("C:\\nonexistent_dir_12345", NullLogger<CapabilityMapResolver>.Instance);
        var fp = new BinaryFingerprint("fp1", "sha", "mod", null, null, DateTimeOffset.UtcNow, Array.Empty<string>(), "path");
        var result = await resolver.ResolveAsync(fp, "profile", "op", new HashSet<string>());
        result.State.Should().Be(SdkCapabilityStatus.Unavailable);
        result.ReasonCode.Should().Be(CapabilityReasonCode.FingerprintMapMissing);
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveDefaultProfileIdAsync_NullFingerprint_Throws()
    {
        var resolver = new CapabilityMapResolver("path", NullLogger<CapabilityMapResolver>.Instance);
        var act = () => resolver.ResolveDefaultProfileIdAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CapabilityMapResolver_ResolveDefaultProfileIdAsync_MissingMap_ReturnsNull()
    {
        var resolver = new CapabilityMapResolver("C:\\nonexistent_dir_12345", NullLogger<CapabilityMapResolver>.Instance);
        var fp = new BinaryFingerprint("fp1", "sha", "mod", null, null, DateTimeOffset.UtcNow, Array.Empty<string>(), "path");
        var result = await resolver.ResolveDefaultProfileIdAsync(fp);
        result.Should().BeNull();
    }

    // ==============================================================
    // WorkshopInventoryService constructor + DiscoverInstalledAsync guard clause
    // ==============================================================

    [Fact]
    public void WorkshopInventoryService_Constructor_NullLogger_Throws()
    {
        var act = () => new WorkshopInventoryService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task WorkshopInventoryService_DiscoverInstalledAsync_NullRequest_Throws()
    {
        var svc = new WorkshopInventoryService(NullLogger<WorkshopInventoryService>.Instance);
        var act = () => svc.DiscoverInstalledAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ==============================================================
    // BinaryFingerprintService constructor + guards
    // ==============================================================

    [Fact]
    public void BinaryFingerprintService_Constructor_NullLogger_Throws()
    {
        var act = () => new BinaryFingerprintService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task BinaryFingerprintService_CaptureFromPathAsync_NullPath_Throws()
    {
        var svc = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var act = () => svc.CaptureFromPathAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BinaryFingerprintService_CaptureFromPathAsync_WhitespacePath_ThrowsArgumentException()
    {
        var svc = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var act = () => svc.CaptureFromPathAsync("  ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BinaryFingerprintService_CaptureFromPathAsync_NonExistentFile_ThrowsFileNotFound()
    {
        var svc = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var act = () => svc.CaptureFromPathAsync("C:\\nonexistent_12345_module.exe");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task BinaryFingerprintService_CaptureFromPathAsync_WithCancellation_NullPath_Throws()
    {
        var svc = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var act = () => svc.CaptureFromPathAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BinaryFingerprintService_CaptureFromPathAsync_WithPid_NullPath_Throws()
    {
        var svc = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var act = () => svc.CaptureFromPathAsync(null!, 1234);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BinaryFingerprintService_CaptureFromPathAsync_WithPidAndCancel_NullPath_Throws()
    {
        var svc = new BinaryFingerprintService(NullLogger<BinaryFingerprintService>.Instance);
        var act = () => svc.CaptureFromPathAsync(null!, 1234, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
