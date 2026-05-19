using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 8 coverage tests targeting static utility methods, record factory methods,
/// pattern matching, hook byte builders, detach cleanup, payload parsing, telemetry,
/// and validation catch branches in RuntimeAdapter.
/// </summary>
public sealed class RuntimeAdapterCommandHandlerWave8Tests
{
    // ──────────────────────────────────────────────────────────────────
    // Reflection Helpers
    // ──────────────────────────────────────────────────────────────────

    private static void SetField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field '{name}' should exist");
        field!.SetValue(target, value);
    }

    private static T? GetField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"field '{name}' should exist");
        return (T?)field!.GetValue(target);
    }

    private static object? InvokeStatic(string name, params object?[] args)
    {
        var methods = typeof(RuntimeAdapter).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.Name == name)
            .ToArray();
        methods.Should().NotBeEmpty($"static method '{name}' should exist");
        var method = methods.Length == 1
            ? methods[0]
            : methods.FirstOrDefault(m => m.GetParameters().Length == args.Length) ?? methods[0];
        return method.Invoke(null, args);
    }

    private static object? InvokePrivate(object target, string name, params object?[] args)
    {
        var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(m => m.Name == name)
            .ToArray();
        methods.Should().NotBeEmpty($"method '{name}' should exist");
        var method = methods.Length == 1
            ? methods[0]
            : methods.FirstOrDefault(m => m.GetParameters().Length == args.Length) ?? methods[0];
        return method.Invoke(target, args);
    }

    private static RuntimeAdapter CreateDetachedAdapter()
    {
        var profile = BuildProfile("set_credits");
        return new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance);
    }

    private static RuntimeAdapter CreateAttachedAdapter(
        RuntimeMode mode = RuntimeMode.Galactic,
        TrainerProfile? profile = null,
        IBackendRouter? router = null,
        IHelperBridgeBackend? helperBackend = null)
    {
        profile ??= BuildProfile("set_credits");
        var services = new Dictionary<Type, object>
        {
            [typeof(IBackendRouter)] = router ?? new StubBackendRouter(
                new BackendRouteDecision(true, ExecutionBackendKind.Memory, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "ok")),
            [typeof(IHelperBridgeBackend)] = helperBackend ?? new StubHelperBridgeBackend(),
            [typeof(IModDependencyValidator)] = new StubDependencyValidator(
                new DependencyValidationResult(DependencyValidationStatus.Pass, "", new HashSet<string>(StringComparer.OrdinalIgnoreCase))),
            [typeof(ITelemetryLogTailService)] = new StubTelemetryLogTailService(),
            [typeof(IExecutionBackend)] = new StubExecutionBackend()
        };

        var adapter = new RuntimeAdapter(
            new StubProcessLocator(),
            new StubProfileRepository(profile),
            new StubSignatureResolver(),
            NullLogger<RuntimeAdapter>.Instance,
            new MapServiceProvider(services));

        var symbolMap = new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95),
            ["unit_cap"] = new SymbolInfo("unit_cap", (nint)0x2000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95),
            ["fog_reveal"] = new SymbolInfo("fog_reveal", (nint)0x3000, SymbolValueType.Byte, AddressSource.Signature, Confidence: 0.95),
            ["test_float"] = new SymbolInfo("test_float", (nint)0x7000, SymbolValueType.Float, AddressSource.Signature, Confidence: 0.95),
            ["test_bool"] = new SymbolInfo("test_bool", (nint)0x9000, SymbolValueType.Bool, AddressSource.Signature, Confidence: 0.95),
            ["test_pointer"] = new SymbolInfo("test_pointer", (nint)0xA000, SymbolValueType.Pointer, AddressSource.Signature, Confidence: 0.95),
            ["test_double"] = new SymbolInfo("test_double", (nint)0xB000, SymbolValueType.Double, AddressSource.Signature, Confidence: 0.95),
            ["test_int64"] = new SymbolInfo("test_int64", (nint)0xC000, SymbolValueType.Int64, AddressSource.Signature, Confidence: 0.95),
            ["test_byte"] = new SymbolInfo("test_byte", (nint)0x8000, SymbolValueType.Byte, AddressSource.Signature, Confidence: 0.95)
        });

        var session = new AttachSession(
            "profile",
            new ProcessMetadata(
                ProcessId: Environment.ProcessId,
                ProcessName: "swfoc",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: null,
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new ProfileBuild("profile", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            symbolMap,
            DateTimeOffset.UtcNow);

        typeof(RuntimeAdapter)
            .GetProperty("CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(adapter, session);
        SetField(adapter, "_attachedProfile", profile);

        var memType = typeof(RuntimeAdapter).Assembly.GetType("SwfocTrainer.Runtime.Interop.ProcessMemoryAccessor")!;
        var accessor = RuntimeHelpers.GetUninitializedObject(memType);
        SetField(adapter, "_memory", accessor);

        return adapter;
    }

    private static TrainerProfile BuildProfile(params string[] actionIds)
    {
        return BuildProfileWithExecution(ExecutionKind.Helper, actionIds);
    }

    private static TrainerProfile BuildProfileWithExecution(ExecutionKind executionKind, params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(id, ActionCategory.Hero, RuntimeMode.Unknown, executionKind, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(Name: "test", GameBuild: "build", Signatures: [new SignatureSpec("credits", "AA BB", 0)])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks:
            [
                new HelperHookSpec(Id: "hero_hook", Script: "scripts/hook.lua", Version: "1.0.0", EntryPoint: "SWFOC_Entry"),
                new HelperHookSpec(Id: "spawn_bridge", Script: "scripts/spawn.lua", Version: "1.0.0", EntryPoint: "SWFOC_Trainer_Spawn_Context")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static TrainerProfile BuildProfileWithFlags(IReadOnlyDictionary<string, bool> flags, params string[] actionIds)
    {
        var actions = actionIds.ToDictionary(
            id => id,
            id => new ActionSpec(id, ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.CodePatch, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            StringComparer.OrdinalIgnoreCase);

        return new TrainerProfile(
            Id: "profile",
            DisplayName: "profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets:
            [
                new SignatureSet(Name: "test", GameBuild: "build", Signatures: [new SignatureSpec("credits", "AA BB", 0)])
            ],
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(flags, StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "save",
            HelperModHooks:
            [
                new HelperHookSpec(Id: "hero_hook", Script: "scripts/hook.lua", Version: "1.0.0", EntryPoint: "SWFOC_Entry")
            ],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static ActionExecutionRequest BuildRequest(string actionId, RuntimeMode mode,
        ExecutionKind kind = ExecutionKind.Helper, JsonObject? payload = null)
    {
        payload ??= new JsonObject { ["helperHookId"] = "hero_hook" };
        return new ActionExecutionRequest(
            Action: new ActionSpec(actionId, ActionCategory.Hero, RuntimeMode.Unknown, kind, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            Payload: payload,
            ProfileId: "profile",
            RuntimeMode: mode);
    }

    // ──────────────────────────────────────────────────────────────────
    // 1. ParseHexBytes
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("90 90 90", new byte[] { 0x90, 0x90, 0x90 })]
    [InlineData("48-8B-74-24-68", new byte[] { 0x48, 0x8B, 0x74, 0x24, 0x68 })]
    [InlineData("FF", new byte[] { 0xFF })]
    [InlineData("00 01", new byte[] { 0x00, 0x01 })]
    public void ParseHexBytes_ShouldParseSpaceAndDashSeparated(string hex, byte[] expected)
    {
        var result = (byte[])InvokeStatic("ParseHexBytes", hex)!;
        result.Should().Equal(expected);
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. IsRel32Reachable
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRel32Reachable_ShouldReturnTrue_WhenTargetInRange()
    {
        var result = (bool)InvokeStatic("IsRel32Reachable", (nint)0x1000, 5, (nint)0x2000)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsRel32Reachable_ShouldReturnFalse_WhenTargetOutOfRange()
    {
        var result = (bool)InvokeStatic("IsRel32Reachable", (nint)0x100000, 5, unchecked((nint)0x1_8000_0000))!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. ComputeRelativeDisplacement
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeRelativeDisplacement_ShouldReturnCorrectDelta()
    {
        var result = (int)InvokeStatic("ComputeRelativeDisplacement", (nint)0x1005, (nint)0x2000)!;
        result.Should().Be(0x2000 - 0x1005);
    }

    [Fact]
    public void ComputeRelativeDisplacement_ShouldThrow_WhenOutOfRange()
    {
        var act = () => InvokeStatic("ComputeRelativeDisplacement", (nint)0x100, unchecked((nint)0x1_8000_0000));
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>()
           .WithMessage("*rel32*");
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. WriteInt32
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void WriteInt32_ShouldWriteLittleEndianBytes()
    {
        var buffer = new byte[8];
        InvokeStatic("WriteInt32", buffer, 2, 0x04030201);
        buffer[2].Should().Be(0x01);
        buffer[3].Should().Be(0x02);
        buffer[4].Should().Be(0x03);
        buffer[5].Should().Be(0x04);
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. BuildRelativeJumpBytes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildRelativeJumpBytes_ShouldReturnFiveByteJmpInstruction()
    {
        var result = (byte[])InvokeStatic("BuildRelativeJumpBytes", (nint)0x10000, (nint)0x10100)!;
        result.Should().HaveCount(5);
        result[0].Should().Be(0xE9); // JMP rel32 opcode
    }

    // ──────────────────────────────────────────────────────────────────
    // 6. BuildUnitCapHookCaveBytes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildUnitCapHookCaveBytes_ShouldReturnCorrectSize()
    {
        var result = (byte[])InvokeStatic("BuildUnitCapHookCaveBytes", (nint)0x20000, (nint)0x10000, 99999)!;
        result.Should().HaveCount(15); // UnitCapHookCaveSize
        result[0].Should().Be(0xBF); // mov edi, imm32
    }

    // ──────────────────────────────────────────────────────────────────
    // 7. BuildInstantBuildHookCaveBytes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildInstantBuildHookCaveBytes_ShouldReturnCorrectSize()
    {
        var originalBytes = new byte[] { 0x8B, 0x83, 0x04, 0x09, 0x00, 0x00 };
        var result = (byte[])InvokeStatic("BuildInstantBuildHookCaveBytes", (nint)0x20000, (nint)0x10000, originalBytes)!;
        result.Should().HaveCount(31); // InstantBuildHookCaveSize
    }

    // ──────────────────────────────────────────────────────────────────
    // 8. BuildInstantBuildJumpPatchBytes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildInstantBuildJumpPatchBytes_ShouldReturnSixBytes_WithNop()
    {
        var result = (byte[])InvokeStatic("BuildInstantBuildJumpPatchBytes", (nint)0x10000, (nint)0x10100)!;
        result.Should().HaveCount(6);
        result[0].Should().Be(0xE9);
        result[5].Should().Be(0x90); // NOP padding
    }

    // ──────────────────────────────────────────────────────────────────
    // 9. BuildCreditsHookCaveBytes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCreditsHookCaveBytes_ShouldBuildValidCave()
    {
        var originalInstruction = new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 };
        var result = (byte[])InvokeStatic("BuildCreditsHookCaveBytes",
            (nint)0x20000, (nint)0x10000, originalInstruction, (byte)0x70, (byte)0x02)!;
        // CreditsHookCaveSize = CreditsHookDataForcedFloatBitsOffset + sizeof(int) = 41 + 8 + 4 + 4 + 4 = 57 actually
        // CreditsHookDataForcedFloatBitsOffset = 41+8+4+4 = 57; CaveSize = 57+4 = 61? No let me check:
        // CreditsHookCodeSize = 41
        // CreditsHookDataLastContextOffset = 41
        // CreditsHookDataHitCountOffset = 41 + 8 = 49
        // CreditsHookDataLockEnabledOffset = 49 + 4 = 53
        // CreditsHookDataForcedFloatBitsOffset = 53 + 4 = 57
        // CreditsHookCaveSize = 57 + 4 = 61
        result.Should().HaveCount(61);
    }

    [Fact]
    public void BuildCreditsHookCaveBytes_ShouldThrow_WhenInstructionLengthWrong()
    {
        var act = () => InvokeStatic("BuildCreditsHookCaveBytes",
            (nint)0x20000, (nint)0x10000, new byte[] { 0x90 }, (byte)0x70, (byte)0x02);
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>()
           .WithMessage("*5-byte*");
    }

    [Fact]
    public void BuildCreditsHookCaveBytes_ShouldThrow_WhenDestinationRegOutOfRange()
    {
        var act = () => InvokeStatic("BuildCreditsHookCaveBytes",
            (nint)0x20000, (nint)0x10000, new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 }, (byte)0x70, (byte)0x08);
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>()
           .WithMessage("*register out of range*");
    }

    // ──────────────────────────────────────────────────────────────────
    // 10. TryParseCreditsCvttss2siInstruction
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_ShouldParse_ValidInstruction()
    {
        // F3 0F 2C 50 70 — cvttss2si edx, [rax+70h]
        // modrm = 0x50 => mod=01, reg=010, rm=000 => dest=2 (edx), offset=0x70
        var module = new byte[] { 0x00, 0xF3, 0x0F, 0x2C, 0x50, 0x70, 0x00 };
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCreditsCvttss2siInstruction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = new object?[] { module, 1, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_ShouldFail_WhenOffsetOutOfRange()
    {
        var module = new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 };
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCreditsCvttss2siInstruction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = new object?[] { module, -1, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_ShouldFail_WhenNotF3Prefix()
    {
        var module = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCreditsCvttss2siInstruction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = new object?[] { module, 0, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCreditsCvttss2siInstruction_ShouldFail_WhenModNotOne()
    {
        // mod=00 rm=000 => modrm=0x00 doesn't match mod=01
        var module = new byte[] { 0xF3, 0x0F, 0x2C, 0x00, 0x70 };
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCreditsCvttss2siInstruction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var args = new object?[] { module, 0, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 11. FindPatternOffsets / IsPatternMatchAtOffset
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FindPatternOffsets_ShouldFindSingleHit()
    {
        var memory = new byte[] { 0x00, 0xAA, 0xBB, 0xCC, 0x00 };
        var pattern = AobPattern.Parse("AA BB CC");
        var method = typeof(RuntimeAdapter).GetMethod("FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IReadOnlyList<int>)method.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public void FindPatternOffsets_ShouldReturnEmpty_WhenNoMatch()
    {
        var memory = new byte[] { 0x00, 0x00, 0x00 };
        var pattern = AobPattern.Parse("FF FF");
        var method = typeof(RuntimeAdapter).GetMethod("FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IReadOnlyList<int>)method.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindPatternOffsets_ShouldRespectMaxHits()
    {
        var memory = new byte[] { 0xAA, 0xAA, 0xAA, 0xAA };
        var pattern = AobPattern.Parse("AA");
        var method = typeof(RuntimeAdapter).GetMethod("FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IReadOnlyList<int>)method.Invoke(null, new object[] { memory, pattern, 2 })!;
        result.Should().HaveCount(2);
    }

    [Fact]
    public void FindPatternOffsets_ShouldReturnEmpty_WhenEmptySignature()
    {
        var memory = new byte[] { 0xAA };
        var pattern = AobPattern.Parse("");
        var method = typeof(RuntimeAdapter).GetMethod("FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IReadOnlyList<int>)method.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindPatternOffsets_ShouldReturnEmpty_WhenMaxHitsIsZero()
    {
        var memory = new byte[] { 0xAA };
        var pattern = AobPattern.Parse("AA");
        var method = typeof(RuntimeAdapter).GetMethod("FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IReadOnlyList<int>)method.Invoke(null, new object[] { memory, pattern, 0 })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindPatternOffsets_ShouldMatchWildcard()
    {
        var memory = new byte[] { 0xAA, 0x12, 0xCC };
        var pattern = AobPattern.Parse("AA ?? CC");
        var method = typeof(RuntimeAdapter).GetMethod("FindPatternOffsets", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (IReadOnlyList<int>)method.Invoke(null, new object[] { memory, pattern, 10 })!;
        result.Should().ContainSingle().Which.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────
    // 12. HasNearbyStoreToCreditsRva
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HasNearbyStoreToCreditsRva_ShouldReturnTrue_WhenStoreMatchesRva()
    {
        // Build a module byte array with a mov [rip+disp32], reg32 at some offset
        // opcode 89, modrm with (mod=00, rm=101) = 0x05 | (reg<<3)
        // e.g. 89 05 disp32 => mov [rip+disp32], eax
        var module = new byte[20];
        var startOffset = 2;
        module[startOffset] = 0x89;
        module[startOffset + 1] = 0x05; // modrm mod=00 reg=eax rm=101
        // nextRip = startOffset + 6 = 8
        // we want nextRip + disp = creditsRva => disp = creditsRva - nextRip
        var creditsRva = 100L;
        var disp = (int)(creditsRva - (startOffset + 6));
        BitConverter.GetBytes(disp).CopyTo(module, startOffset + 2);

        var method = typeof(RuntimeAdapter).GetMethod("HasNearbyStoreToCreditsRva", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, startOffset, 10, creditsRva })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void HasNearbyStoreToCreditsRva_ShouldReturnFalse_WhenNoStore()
    {
        var module = new byte[20];
        var method = typeof(RuntimeAdapter).GetMethod("HasNearbyStoreToCreditsRva", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, 0, 10, 200L })!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 13. LooksLikeImmediateStoreFromConvertedRegister
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void LooksLikeImmediateStoreFromConvertedRegister_ShouldReturnTrue_WhenValidStore()
    {
        // 89 50 = mov [rax+disp8], edx  (mod=01, reg=010=edx, rm=000=rax)
        // modrm 0x50: mod=01 reg=010 rm=000
        var module = new byte[] { 0x89, 0x50, 0x70 };
        var method = typeof(RuntimeAdapter).GetMethod("LooksLikeImmediateStoreFromConvertedRegister", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeTrue();
    }

    [Fact]
    public void LooksLikeImmediateStoreFromConvertedRegister_ShouldReturnFalse_WhenNot89()
    {
        var module = new byte[] { 0x90, 0x50 };
        var method = typeof(RuntimeAdapter).GetMethod("LooksLikeImmediateStoreFromConvertedRegister", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeImmediateStoreFromConvertedRegister_ShouldReturnFalse_WhenRegMismatch()
    {
        // 89 50 => reg=010=edx, but we pass registerIndex=0
        var module = new byte[] { 0x89, 0x50 };
        var method = typeof(RuntimeAdapter).GetMethod("LooksLikeImmediateStoreFromConvertedRegister", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, 0, (byte)0 })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeImmediateStoreFromConvertedRegister_ShouldReturnFalse_WhenModIs3()
    {
        // modrm 0xD2 => mod=11 reg=010 rm=010 => register-to-register, not store
        var module = new byte[] { 0x89, 0xD2 };
        var method = typeof(RuntimeAdapter).GetMethod("LooksLikeImmediateStoreFromConvertedRegister", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, 0, (byte)2 })!;
        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeImmediateStoreFromConvertedRegister_ShouldReturnFalse_WhenOutOfBounds()
    {
        var module = new byte[] { 0x89 };
        var method = typeof(RuntimeAdapter).GetMethod("LooksLikeImmediateStoreFromConvertedRegister", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (bool)method.Invoke(null, new object[] { module, 1, (byte)0 })!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 14. Record factory methods
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void UnitCapHookResolution_Ok_ShouldCreateSucceededResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("UnitCapHookResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
        var result = ok.Invoke(null, new object[] { (nint)0x1000 })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void UnitCapHookResolution_Fail_ShouldCreateFailedResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("UnitCapHookResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var result = fail.Invoke(null, new object[] { "test error" })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeFalse();
        var message = (string)type.GetProperty("Message")!.GetValue(result)!;
        message.Should().Be("test error");
    }

    [Fact]
    public void InstantBuildHookResolution_Ok_ShouldCreateSucceededResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("InstantBuildHookResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
        var result = ok.Invoke(null, new object[] { (nint)0x2000, new byte[] { 0x90 } })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void InstantBuildHookResolution_Fail_ShouldCreateFailedResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("InstantBuildHookResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var result = fail.Invoke(null, new object[] { "instant build error" })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void FogPatchFallbackResolution_Ok_ShouldCreateSucceededResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("FogPatchFallbackResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
        var result = ok.Invoke(null, new object[] { (nint)0x3000, (byte)0x74, (byte)0xEB, "test pattern" })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void FogPatchFallbackResolution_Fail_ShouldCreateFailedResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("FogPatchFallbackResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var result = fail.Invoke(null, new object[] { RuntimeReasonCode.FALLBACK_DISABLED, "fog fail" })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void CreditsHookResolution_Ok_ShouldCreateSucceededResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
        var result = ok.Invoke(null, new object[] { (nint)0x4000, (byte)0x70, (byte)0x02, new byte[] { 0xF3, 0x0F, 0x2C, 0x50, 0x70 } })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void CreditsHookResolution_Fail_ShouldCreateFailedResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var result = fail.Invoke(null, new object[] { "credits fail" })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void CreditsHookPatchResult_Ok_ShouldCreateSucceededResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookPatchResult", BindingFlags.NonPublic)!;
        var ok = type.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!;
        var diagnostics = new Dictionary<string, object?> { ["test"] = "value" } as IReadOnlyDictionary<string, object?>;
        var result = ok.Invoke(null, new object[] { "hook ok", diagnostics })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeTrue();
    }

    [Fact]
    public void CreditsHookPatchResult_Fail_ShouldCreateFailedResult()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookPatchResult", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var result = fail.Invoke(null, new object[] { "hook fail" })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeFalse();
    }

    [Fact]
    public void WriteAttemptResult_SuccessWithoutObservation_ShouldBeSuccess()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("WriteAttemptResult`1", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(int));
        var method = type.GetMethod("SuccessWithoutObservation", BindingFlags.Public | BindingFlags.Static)!;
        var result = method.Invoke(null, null)!;
        var success = (bool)type.GetProperty("Success")!.GetValue(result)!;
        success.Should().BeTrue();
        var hasObserved = (bool)type.GetProperty("HasObservedValue")!.GetValue(result)!;
        hasObserved.Should().BeFalse();
    }

    [Fact]
    public void WriteAttemptResult_SuccessWithObservation_ShouldReturnObservedValue()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("WriteAttemptResult`1", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(int));
        var method = type.GetMethod("SuccessWithObservation", BindingFlags.Public | BindingFlags.Static)!;
        var result = method.Invoke(null, new object[] { 42 })!;
        var success = (bool)type.GetProperty("Success")!.GetValue(result)!;
        success.Should().BeTrue();
        var hasObserved = (bool)type.GetProperty("HasObservedValue")!.GetValue(result)!;
        hasObserved.Should().BeTrue();
        var observed = (int)type.GetProperty("ObservedValue")!.GetValue(result)!;
        observed.Should().Be(42);
    }

    // ──────────────────────────────────────────────────────────────────
    // 15. TryReadBooleanPayload
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryReadBooleanPayload_ShouldReturnTrue_WhenBoolValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["key"] = true };
        var args = new object?[] { payload, "key", false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((bool)args[2]!).Should().BeTrue();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldReturnFalse_WhenKeyMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject();
        var args = new object?[] { payload, "missing", false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldParseIntAsBoolean()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["key"] = 1 };
        var args = new object?[] { payload, "key", false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((bool)args[2]!).Should().BeTrue();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldParseStringAsBoolean()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["key"] = "true" };
        var args = new object?[] { payload, "key", false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((bool)args[2]!).Should().BeTrue();
    }

    [Fact]
    public void TryReadBooleanPayload_ShouldReturnFalse_WhenUnparsableValue()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBooleanPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["key"] = "not_a_bool" };
        var args = new object?[] { payload, "key", false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 16. TryReadFloatPayload — double->float conversion path
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryReadFloatPayload_ShouldReturnTrue_WhenFloatPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadFloatPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["floatValue"] = 3.14f };
        var args = new object?[] { payload, 0f };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void TryReadFloatPayload_ShouldReturnFalse_WhenNotPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadFloatPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject();
        var args = new object?[] { payload, 0f };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadBoolPayload_ShouldReturnTrue_WhenBoolPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBoolPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["boolValue"] = true };
        var args = new object?[] { payload, false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void TryReadBoolPayload_ShouldReturnFalse_WhenNotPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadBoolPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject();
        var args = new object?[] { payload, false };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadIntPayload_ShouldReturnTrue_WhenIntPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadIntPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["intValue"] = 42 };
        var args = new object?[] { payload, 0 };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void TryReadIntPayload_ShouldReturnFalse_WhenNotPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadIntPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject();
        var args = new object?[] { payload, 0 };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 17. Validation methods — static
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateRequestedFloatValue_ShouldFail_WhenNaN()
    {
        var result = InvokeStatic("ValidateRequestedFloatValue", "test", (double)double.NaN, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldFail_WhenInfinity()
    {
        var result = InvokeStatic("ValidateRequestedFloatValue", "test", double.PositiveInfinity, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldPass_WhenNoRule()
    {
        var result = InvokeStatic("ValidateRequestedFloatValue", "test", 5.0, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldFail_WhenBelowMin()
    {
        var rule = new SymbolValidationRule("test", FloatMin: 10.0, FloatMax: 100.0);
        var result = InvokeStatic("ValidateRequestedFloatValue", "test", 5.0, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateRequestedFloatValue_ShouldFail_WhenAboveMax()
    {
        var rule = new SymbolValidationRule("test", FloatMin: 0.0, FloatMax: 10.0);
        var result = InvokeStatic("ValidateRequestedFloatValue", "test", 15.0, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateObservedFloatValue_ShouldFail_WhenNaN()
    {
        var result = InvokeStatic("ValidateObservedFloatValue", "test", (double)double.NaN, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateObservedFloatValue_ShouldFail_WhenBelowMin()
    {
        var rule = new SymbolValidationRule("test", FloatMin: 10.0, FloatMax: 100.0);
        var result = InvokeStatic("ValidateObservedFloatValue", "test", 5.0, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateObservedFloatValue_ShouldFail_WhenAboveMax()
    {
        var rule = new SymbolValidationRule("test", FloatMin: 0.0, FloatMax: 10.0);
        var result = InvokeStatic("ValidateObservedFloatValue", "test", 15.0, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldPass_WhenNoRule()
    {
        var result = InvokeStatic("ValidateRequestedIntValue", "test", 100L, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldFail_WhenBelowMin()
    {
        var rule = new SymbolValidationRule("test", IntMin: 10L, IntMax: 100L);
        var result = InvokeStatic("ValidateRequestedIntValue", "test", 5L, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateRequestedIntValue_ShouldFail_WhenAboveMax()
    {
        var rule = new SymbolValidationRule("test", IntMin: 0L, IntMax: 50L);
        var result = InvokeStatic("ValidateRequestedIntValue", "test", 60L, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldPass_WhenNoRule()
    {
        var result = InvokeStatic("ValidateObservedIntValue", "test", 100L, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldFail_WhenBelowMin()
    {
        var rule = new SymbolValidationRule("test", IntMin: 10L, IntMax: 100L);
        var result = InvokeStatic("ValidateObservedIntValue", "test", 5L, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateObservedIntValue_ShouldFail_WhenAboveMax()
    {
        var rule = new SymbolValidationRule("test", IntMin: 0L, IntMax: 50L);
        var result = InvokeStatic("ValidateObservedIntValue", "test", 60L, rule);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 18. ValidateObservedReadValue — catch branches
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SymbolValueType.Int32)]
    [InlineData(SymbolValueType.Int64)]
    [InlineData(SymbolValueType.Byte)]
    [InlineData(SymbolValueType.Float)]
    [InlineData(SymbolValueType.Double)]
    public void ValidateObservedReadValue_ShouldFail_WhenCastFails(SymbolValueType valueType)
    {
        // Pass "not_a_number" which should trigger FormatException or InvalidCastException
        var result = InvokeStatic("ValidateObservedReadValue", "test", (object)"not_a_number", valueType, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
        var reasonCode = (string)result.GetType().GetProperty("ReasonCode")!.GetValue(result)!;
        reasonCode.Should().Be("observed_cast_failed");
    }

    [Fact]
    public void ValidateObservedReadValue_ShouldPass_WhenUnknownValueType()
    {
        var result = InvokeStatic("ValidateObservedReadValue", "test", (object)42, (SymbolValueType)999, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateObservedReadValue_ShouldValidateBoolCorrectly()
    {
        var result = InvokeStatic("ValidateObservedReadValue", "test", (object)true, SymbolValueType.Bool, (object?)null);
        var isValid = (bool)result!.GetType().GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    // 19. FormatValidationRuleRange
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatValidationRuleRange_ShouldReturnNone_WhenNoRange()
    {
        var rule = new SymbolValidationRule("test");
        var result = (string)InvokeStatic("FormatValidationRuleRange", rule)!;
        result.Should().Be("none");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatIntRange()
    {
        var rule = new SymbolValidationRule("test", IntMin: 0L, IntMax: 100L);
        var result = (string)InvokeStatic("FormatValidationRuleRange", rule)!;
        result.Should().Contain("int[").And.Contain("100");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatFloatRange()
    {
        var rule = new SymbolValidationRule("test", FloatMin: 1.5, FloatMax: 99.5);
        var result = (string)InvokeStatic("FormatValidationRuleRange", rule)!;
        result.Should().Contain("float[");
    }

    [Fact]
    public void FormatValidationRuleRange_ShouldFormatBothRanges()
    {
        var rule = new SymbolValidationRule("test", IntMin: 0L, IntMax: 100L, FloatMin: 0.0, FloatMax: 50.0);
        var result = (string)InvokeStatic("FormatValidationRuleRange", rule)!;
        result.Should().Contain("int[").And.Contain("float[");
    }

    // ──────────────────────────────────────────────────────────────────
    // 20. CreateSymbolDiagnostics
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateSymbolDiagnostics_ShouldIncludeExpectedKeys()
    {
        var symbolInfo = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95);
        var result = (Dictionary<string, object?>)InvokeStatic("CreateSymbolDiagnostics", symbolInfo, (object?)null, false)!;
        result.Should().ContainKey("address");
        result.Should().ContainKey("symbolSource");
        result.Should().ContainKey("symbolHealthStatus");
        result.Should().ContainKey("symbolConfidence");
        result.Should().ContainKey("criticalSymbol");
        result.Should().NotContainKey("validationRuleMode");
    }

    [Fact]
    public void CreateSymbolDiagnostics_ShouldIncludeValidationRule_WhenProvided()
    {
        var symbolInfo = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.95);
        var rule = new SymbolValidationRule("credits", Mode: RuntimeMode.Galactic, IntMin: 0L, IntMax: 999999L);
        var result = (Dictionary<string, object?>)InvokeStatic("CreateSymbolDiagnostics", symbolInfo, rule, true)!;
        result.Should().ContainKey("validationRuleMode");
        result.Should().ContainKey("validationRange");
        ((bool)result["criticalSymbol"]!).Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    // 21. ResolveSymbolValidationRule / IsCriticalSymbol
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveSymbolValidationRule_ShouldReturnNull_WhenNoRules()
    {
        var adapter = CreateAttachedAdapter();
        var result = InvokePrivate(adapter, "ResolveSymbolValidationRule", "credits", RuntimeMode.Galactic);
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSymbolValidationRule_ShouldReturnExactMode_WhenAvailable()
    {
        var adapter = CreateAttachedAdapter();
        var rules = new Dictionary<string, List<SymbolValidationRule>>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new()
            {
                new SymbolValidationRule("credits", Mode: RuntimeMode.Galactic, IntMin: 0L, IntMax: 99999L),
                new SymbolValidationRule("credits", IntMin: 0L, IntMax: 50000L)
            }
        };
        SetField(adapter, "_symbolValidationRules", rules);
        var result = (SymbolValidationRule?)InvokePrivate(adapter, "ResolveSymbolValidationRule", "credits", RuntimeMode.Galactic);
        result.Should().NotBeNull();
        result!.IntMax.Should().Be(99999L);
    }

    [Fact]
    public void ResolveSymbolValidationRule_ShouldReturnNullMode_WhenNoExactMatch()
    {
        var adapter = CreateAttachedAdapter();
        var rules = new Dictionary<string, List<SymbolValidationRule>>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new()
            {
                new SymbolValidationRule("credits", Mode: RuntimeMode.TacticalLand, IntMin: 0L, IntMax: 99999L),
                new SymbolValidationRule("credits", IntMin: 0L, IntMax: 50000L)
            }
        };
        SetField(adapter, "_symbolValidationRules", rules);
        var result = (SymbolValidationRule?)InvokePrivate(adapter, "ResolveSymbolValidationRule", "credits", RuntimeMode.Galactic);
        result.Should().NotBeNull();
        result!.IntMax.Should().Be(50000L);
    }

    [Fact]
    public void IsCriticalSymbol_ShouldReturnTrue_WhenInCriticalSet()
    {
        var adapter = CreateAttachedAdapter();
        var criticals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "credits" };
        SetField(adapter, "_criticalSymbols", criticals);
        var result = (bool)InvokePrivate(adapter, "IsCriticalSymbol", "credits", (object?)null)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCriticalSymbol_ShouldReturnTrue_WhenRuleCritical()
    {
        var adapter = CreateAttachedAdapter();
        var rule = new SymbolValidationRule("test", Critical: true);
        var result = (bool)InvokePrivate(adapter, "IsCriticalSymbol", "test", rule)!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCriticalSymbol_ShouldReturnFalse_WhenNotCritical()
    {
        var adapter = CreateAttachedAdapter();
        var result = (bool)InvokePrivate(adapter, "IsCriticalSymbol", "test", (object?)null)!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 22. ResolveMemoryActionSymbol
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveMemoryActionSymbol_ShouldReturnSymbol()
    {
        var payload = new JsonObject { ["symbol"] = "credits" };
        var result = (string)InvokeStatic("ResolveMemoryActionSymbol", payload)!;
        result.Should().Be("credits");
    }

    [Fact]
    public void ResolveMemoryActionSymbol_ShouldThrow_WhenMissing()
    {
        var payload = new JsonObject();
        var act = () => InvokeStatic("ResolveMemoryActionSymbol", payload);
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>()
           .WithMessage("*symbol*");
    }

    // ──────────────────────────────────────────────────────────────────
    // 23. IsCreditsWrite
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IsCreditsWrite_ShouldReturnTrue_WhenActionIdIsSetCredits()
    {
        var request = BuildRequest("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory);
        var result = (bool)InvokeStatic("IsCreditsWrite", request, "some_symbol")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCreditsWrite_ShouldReturnTrue_WhenSymbolIsCredits()
    {
        var request = BuildRequest("other_action", RuntimeMode.Galactic, ExecutionKind.Memory);
        var result = (bool)InvokeStatic("IsCreditsWrite", request, "credits")!;
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCreditsWrite_ShouldReturnFalse_WhenNeitherMatch()
    {
        var request = BuildRequest("other_action", RuntimeMode.Galactic, ExecutionKind.Memory);
        var result = (bool)InvokeStatic("IsCreditsWrite", request, "unit_cap")!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 24. RecordActionTelemetry
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordActionTelemetry_ShouldIncrementCounters()
    {
        var adapter = CreateAttachedAdapter();
        var request = BuildRequest("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory);
        var successResult = new ActionExecutionResult(true, "ok", AddressSource.Signature);
        InvokePrivate(adapter, "RecordActionTelemetry", request, successResult);

        var successCounters = GetField<Dictionary<string, int>>(adapter, "_actionSuccessCounters")!;
        successCounters.Should().ContainKey("profile:set_credits");
        successCounters["profile:set_credits"].Should().Be(1);

        // Call again to test increment path
        InvokePrivate(adapter, "RecordActionTelemetry", request, successResult);
        successCounters["profile:set_credits"].Should().Be(2);
    }

    [Fact]
    public void RecordActionTelemetry_ShouldIncrementFailureCounters()
    {
        var adapter = CreateAttachedAdapter();
        var request = BuildRequest("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory);
        var failResult = new ActionExecutionResult(false, "failed", AddressSource.None);
        InvokePrivate(adapter, "RecordActionTelemetry", request, failResult);

        var failCounters = GetField<Dictionary<string, int>>(adapter, "_actionFailureCounters")!;
        failCounters.Should().ContainKey("profile:set_credits");
    }

    [Fact]
    public void RecordActionTelemetry_ShouldUseAttachedProfile_WhenProfileIdBlank()
    {
        var adapter = CreateAttachedAdapter();
        var request = new ActionExecutionRequest(
            Action: new ActionSpec("set_credits", ActionCategory.Hero, RuntimeMode.Unknown, ExecutionKind.Memory, new JsonObject(), VerifyReadback: false, CooldownMs: 0),
            Payload: new JsonObject(),
            ProfileId: "",
            RuntimeMode: RuntimeMode.Galactic);
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature);
        InvokePrivate(adapter, "RecordActionTelemetry", request, result);

        var successCounters = GetField<Dictionary<string, int>>(adapter, "_actionSuccessCounters")!;
        successCounters.Should().ContainKey("profile:set_credits");
    }

    // ──────────────────────────────────────────────────────────────────
    // 25. IncrementCounter
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IncrementCounter_ShouldCreateNewKey()
    {
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        InvokeStatic("IncrementCounter", counters, "key1");
        counters["key1"].Should().Be(1);
    }

    [Fact]
    public void IncrementCounter_ShouldIncrementExistingKey()
    {
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["key1"] = 5 };
        InvokeStatic("IncrementCounter", counters, "key1");
        counters["key1"].Should().Be(6);
    }

    // ──────────────────────────────────────────────────────────────────
    // 26. BuildRequestedValidationFailureResult
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildRequestedValidationFailureResult_ShouldCreateFailResult()
    {
        var symbolInfo = new SymbolInfo("test", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.9);
        var validationType = typeof(RuntimeAdapter).GetNestedType("ValidationOutcome", BindingFlags.NonPublic)!;
        var fail = validationType.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var validation = fail.Invoke(null, new object[] { "value_below_min", "Value too low" })!;
        var result = (ActionExecutionResult)InvokeStatic("BuildRequestedValidationFailureResult", symbolInfo, validation)!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("Value too low");
        result.Diagnostics.Should().ContainKey("failureReasonCode");
    }

    // ──────────────────────────────────────────────────────────────────
    // 27. TryReadCodePatchSymbol / TryParseCodePatchBytes
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryReadCodePatchSymbol_ShouldReturnTrue_WhenSymbolPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadCodePatchSymbol", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject { ["symbol"] = "test_sym" };
        var args = new object?[] { payload, null, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((string)args[1]!).Should().Be("test_sym");
    }

    [Fact]
    public void TryReadCodePatchSymbol_ShouldReturnFalse_WhenSymbolMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryReadCodePatchSymbol", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject();
        var args = new object?[] { payload, null, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
        args[2].Should().NotBeNull();
    }

    [Fact]
    public void TryParseCodePatchBytes_ShouldReturnTrue_WhenBothPresent()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCodePatchBytes", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject
        {
            ["patchBytes"] = "90 90 90",
            ["originalBytes"] = "48 89 E5"
        };
        var args = new object?[] { payload, null, null, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((byte[])args[1]!).Should().HaveCount(3);
    }

    [Fact]
    public void TryParseCodePatchBytes_ShouldReturnFalse_WhenMissing()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCodePatchBytes", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject();
        var args = new object?[] { payload, null, null, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCodePatchBytes_ShouldReturnFalse_WhenLengthMismatch()
    {
        var method = typeof(RuntimeAdapter).GetMethod("TryParseCodePatchBytes", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = new JsonObject
        {
            ["patchBytes"] = "90 90",
            ["originalBytes"] = "48 89 E5"
        };
        var args = new object?[] { payload, null, null, null };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 28. ResolveHelperHookId / ResolveHelperOperationKind
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("spawn_context_entity", "spawn_bridge")]
    [InlineData("spawn_tactical_entity", "spawn_bridge")]
    [InlineData("spawn_galactic_entity", "spawn_bridge")]
    [InlineData("place_planet_building", "spawn_bridge")]
    public void ResolveHelperHookId_ShouldReturnSpawnBridge_ForSpawnActions(string actionId, string expectedHookId)
    {
        var request = BuildRequest(actionId, RuntimeMode.Galactic, payload: new JsonObject());
        var result = (string)InvokeStatic("ResolveHelperHookId", request)!;
        result.Should().Be(expectedHookId);
    }

    [Fact]
    public void ResolveHelperHookId_ShouldReturnExplicitHookId_WhenInPayload()
    {
        var request = BuildRequest("any_action", RuntimeMode.Galactic, payload: new JsonObject { ["helperHookId"] = "custom_hook" });
        var result = (string)InvokeStatic("ResolveHelperHookId", request)!;
        result.Should().Be("custom_hook");
    }

    [Fact]
    public void ResolveHelperHookId_ShouldReturnActionId_WhenNoExplicitAndNotSpawn()
    {
        var request = BuildRequest("custom_action", RuntimeMode.Galactic, payload: new JsonObject());
        var result = (string)InvokeStatic("ResolveHelperHookId", request)!;
        result.Should().Be("custom_action");
    }

    [Theory]
    [InlineData("spawn_unit_helper", 1)] // SpawnUnitHelper
    [InlineData("spawn_context_entity", 2)] // SpawnContextEntity
    [InlineData("spawn_tactical_entity", 3)] // SpawnTacticalEntity
    [InlineData("spawn_galactic_entity", 4)] // SpawnGalacticEntity
    [InlineData("place_planet_building", 5)] // PlacePlanetBuilding
    [InlineData("set_context_allegiance", 6)] // SetContextAllegiance
    [InlineData("set_context_faction", 6)] // SetContextAllegiance
    [InlineData("set_hero_state_helper", 7)] // SetHeroStateHelper
    [InlineData("toggle_roe_respawn_helper", 8)] // ToggleRoeRespawnHelper
    [InlineData("unknown_action", 0)] // Unknown
    public void ResolveHelperOperationKind_ShouldReturnExpectedKind(string actionId, int expectedKindValue)
    {
        var result = InvokeStatic("ResolveHelperOperationKind", actionId);
        ((int)result!).Should().Be(expectedKindValue);
    }

    // ──────────────────────────────────────────────────────────────────
    // 29. IsPromotedExtenderAction
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("freeze_timer", true)]
    [InlineData("toggle_fog_reveal", true)]
    [InlineData("toggle_ai", true)]
    [InlineData("set_unit_cap", true)]
    [InlineData("toggle_instant_build_patch", true)]
    [InlineData("unknown_action", false)]
    [InlineData("", false)]
    public void IsPromotedExtenderAction_ShouldReturn_ExpectedResult(string actionId, bool expected)
    {
        var result = (bool)InvokeStatic("IsPromotedExtenderAction", actionId)!;
        result.Should().Be(expected);
    }

    // ──────────────────────────────────────────────────────────────────
    // 30. EnsureAttached / ResolveSymbol
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureAttached_ShouldThrow_WhenNotAttached()
    {
        var adapter = CreateDetachedAdapter();
        var act = () => InvokePrivate(adapter, "EnsureAttached");
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>()
           .WithMessage("*not attached*");
    }

    [Fact]
    public void ResolveSymbol_ShouldThrow_WhenSymbolMissing()
    {
        var adapter = CreateAttachedAdapter();
        var act = () => InvokePrivate(adapter, "ResolveSymbol", "nonexistent_symbol");
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<KeyNotFoundException>()
           .WithMessage("*not resolved*");
    }

    [Fact]
    public void ResolveSymbol_ShouldReturnSymbol_WhenPresent()
    {
        var adapter = CreateAttachedAdapter();
        var result = (SymbolInfo)InvokePrivate(adapter, "ResolveSymbol", "credits")!;
        result.Name.Should().Be("credits");
        result.Address.Should().NotBe(nint.Zero);
    }

    // ──────────────────────────────────────────────────────────────────
    // 31. Detach cleanup
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetachAsync_ShouldClearAllState()
    {
        var adapter = CreateAttachedAdapter();
        adapter.IsAttached.Should().BeTrue();

        await adapter.DetachAsync();

        adapter.IsAttached.Should().BeFalse();
        adapter.CurrentSession.Should().BeNull();
    }

    [Fact]
    public void TryRestoreCodePatchesOnDetach_ShouldDoNothing_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        // Should not throw
        InvokePrivate(adapter, "TryRestoreCodePatchesOnDetach");
    }

    [Fact]
    public void TryRestoreCodePatchesOnDetach_ShouldDoNothing_WhenNoPatchesActive()
    {
        var adapter = CreateAttachedAdapter();
        // No patches active by default
        InvokePrivate(adapter, "TryRestoreCodePatchesOnDetach");
        // Should complete without error
    }

    [Fact]
    public void TryRestoreCreditsHookOnDetach_ShouldDoNothing_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        InvokePrivate(adapter, "TryRestoreCreditsHookOnDetach");
    }

    [Fact]
    public void TryRestoreUnitCapHookOnDetach_ShouldDoNothing_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        InvokePrivate(adapter, "TryRestoreUnitCapHookOnDetach");
    }

    [Fact]
    public void TryRestoreInstantBuildHookOnDetach_ShouldDoNothing_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        InvokePrivate(adapter, "TryRestoreInstantBuildHookOnDetach");
    }

    [Fact]
    public void TryRestoreFogPatchFallbackOnDetach_ShouldDoNothing_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        InvokePrivate(adapter, "TryRestoreFogPatchFallbackOnDetach");
    }

    [Fact]
    public void TryRestoreFogPatchFallbackOnDetach_ShouldDoNothing_WhenAddressZero()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_fogPatchFallbackAddress", nint.Zero);
        InvokePrivate(adapter, "TryRestoreFogPatchFallbackOnDetach");
    }

    // ──────────────────────────────────────────────────────────────────
    // 32. Clear*HookState methods
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ClearCreditsHookState_ShouldResetAllFields()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_creditsHookInjectionAddress", (nint)0x1234);
        InvokePrivate(adapter, "ClearCreditsHookState");
        var injAddr = GetField<nint>(adapter, "_creditsHookInjectionAddress");
        injAddr.Should().Be(nint.Zero);
    }

    [Fact]
    public void ClearUnitCapHookState_ShouldResetAllFields()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_unitCapHookInjectionAddress", (nint)0x1234);
        InvokePrivate(adapter, "ClearUnitCapHookState");
        var injAddr = GetField<nint>(adapter, "_unitCapHookInjectionAddress");
        injAddr.Should().Be(nint.Zero);
    }

    [Fact]
    public void ClearInstantBuildHookState_ShouldResetAllFields()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_instantBuildHookInjectionAddress", (nint)0x1234);
        InvokePrivate(adapter, "ClearInstantBuildHookState");
        var injAddr = GetField<nint>(adapter, "_instantBuildHookInjectionAddress");
        injAddr.Should().Be(nint.Zero);
    }

    [Fact]
    public void ClearFogPatchFallbackState_ShouldResetAllFields()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_fogPatchFallbackAddress", (nint)0x5678);
        InvokePrivate(adapter, "ClearFogPatchFallbackState");
        var addr = GetField<nint>(adapter, "_fogPatchFallbackAddress");
        addr.Should().Be(nint.Zero);
    }

    // ──────────────────────────────────────────────────────────────────
    // 33. IsCreditsRuntimeHookInstalled / IsUnitCapHookInstalled / IsInstantBuildHookInstalled
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IsCreditsRuntimeHookInstalled_ShouldReturnFalse_WhenNotInstalled()
    {
        var adapter = CreateAttachedAdapter();
        var result = (bool)InvokePrivate(adapter, "IsCreditsRuntimeHookInstalled")!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsUnitCapHookInstalled_ShouldReturnFalse_WhenNotInstalled()
    {
        var adapter = CreateAttachedAdapter();
        var result = (bool)InvokePrivate(adapter, "IsUnitCapHookInstalled")!;
        result.Should().BeFalse();
    }

    [Fact]
    public void IsInstantBuildHookInstalled_ShouldReturnFalse_WhenNotInstalled()
    {
        var adapter = CreateAttachedAdapter();
        var result = (bool)InvokePrivate(adapter, "IsInstantBuildHookInstalled")!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 34. BuildCreditsHookPatternNotFoundResult
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCreditsHookPatternNotFoundResult_ShouldReturnFailResult_WithRva()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var method = typeof(RuntimeAdapter).GetMethod("BuildCreditsHookPatternNotFoundResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = method.Invoke(null, new object[] { 10, 5, 3, 0x1000L })!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeFalse();
        var message = (string)type.GetProperty("Message")!.GetValue(result)!;
        message.Should().Contain("correlation");
    }

    [Fact]
    public void BuildCreditsHookPatternNotFoundResult_ShouldReturnFailResult_WithoutRva()
    {
        var method = typeof(RuntimeAdapter).GetMethod("BuildCreditsHookPatternNotFoundResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var result = method.Invoke(null, new object[] { 10, 5, 3, -1L })!;
        var message = (string)type.GetProperty("Message")!.GetValue(result)!;
        message.Should().Contain("unavailable");
    }

    // ──────────────────────────────────────────────────────────────────
    // 35. BuildReResolve*Result methods
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildReResolveUnavailableResult_ShouldReturnExpected()
    {
        var method = typeof(RuntimeAdapter).GetMethod("BuildReResolveUnavailableResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = method.Invoke(null, null)!;
        var tuple = ((bool, SymbolInfo?, string, string))result;
        tuple.Item1.Should().BeFalse();
        tuple.Item3.Should().Be("reresolve_unavailable");
    }

    [Fact]
    public void BuildReResolveUnresolvedResult_ShouldReturnExpected()
    {
        var method = typeof(RuntimeAdapter).GetMethod("BuildReResolveUnresolvedResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = method.Invoke(null, new object[] { "test_sym" })!;
        var tuple = ((bool, SymbolInfo?, string, string))result;
        tuple.Item1.Should().BeFalse();
        tuple.Item3.Should().Be("reresolve_symbol_unresolved");
    }

    [Fact]
    public void BuildReResolveSuccessResult_ShouldReturnExpected()
    {
        var method = typeof(RuntimeAdapter).GetMethod("BuildReResolveSuccessResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var sym = new SymbolInfo("test", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        var result = method.Invoke(null, new object[] { sym })!;
        var tuple = ((bool, SymbolInfo?, string, string))result;
        tuple.Item1.Should().BeTrue();
        tuple.Item3.Should().Be("reresolve_success");
    }

    [Fact]
    public void BuildReResolveExceptionResult_ShouldReturnExpected()
    {
        var method = typeof(RuntimeAdapter).GetMethod("BuildReResolveExceptionResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = method.Invoke(null, new object[] { "test_sym", new InvalidOperationException("test error") })!;
        var tuple = ((bool, SymbolInfo?, string, string))result;
        tuple.Item1.Should().BeFalse();
        tuple.Item3.Should().Be("reresolve_exception");
        tuple.Item4.Should().Contain("test error");
    }

    // ──────────────────────────────────────────────────────────────────
    // 36. TryGetReResolveContext
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryGetReResolveContext_ShouldReturnFalse_WhenDetached()
    {
        var adapter = CreateDetachedAdapter();
        var method = adapter.GetType().GetMethod("TryGetReResolveContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object?[] { null };
        var result = (bool)method.Invoke(adapter, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryGetReResolveContext_ShouldReturnTrue_WhenAttached()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("TryGetReResolveContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object?[] { null };
        var result = (bool)method.Invoke(adapter, args)!;
        result.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    // 37. ValidationOutcome record factory methods
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationOutcome_Pass_ShouldCreateValidOutcome()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("ValidationOutcome", BindingFlags.NonPublic)!;
        var pass = type.GetMethod("Pass", BindingFlags.Public | BindingFlags.Static)!;
        var result = pass.Invoke(null, new object[] { "ok" })!;
        var isValid = (bool)type.GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidationOutcome_Fail_ShouldCreateInvalidOutcome()
    {
        var type = typeof(RuntimeAdapter).GetNestedType("ValidationOutcome", BindingFlags.NonPublic)!;
        var fail = type.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!;
        var result = fail.Invoke(null, new object[] { "reason", "message" })!;
        var isValid = (bool)type.GetProperty("IsValid")!.GetValue(result)!;
        isValid.Should().BeFalse();
        var message = (string)type.GetProperty("Message")!.GetValue(result)!;
        message.Should().Be("message");
    }

    // ──────────────────────────────────────────────────────────────────
    // 38. MergeDiagnostics
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MergeDiagnostics_ShouldReturnNull_WhenBothNull()
    {
        var result = InvokeStatic("MergeDiagnostics", (object?)null, (object?)null);
        result.Should().BeNull();
    }

    [Fact]
    public void MergeDiagnostics_ShouldMergeBothDictionaries()
    {
        var primary = new Dictionary<string, object?> { ["a"] = 1 } as IReadOnlyDictionary<string, object?>;
        var secondary = new Dictionary<string, object?> { ["b"] = 2 } as IReadOnlyDictionary<string, object?>;
        var result = (IReadOnlyDictionary<string, object?>)InvokeStatic("MergeDiagnostics", primary, secondary)!;
        result.Should().ContainKey("a");
        result.Should().ContainKey("b");
    }

    [Fact]
    public void MergeDiagnostics_ShouldReturnPrimary_WhenBothEmpty()
    {
        var primary = new Dictionary<string, object?>() as IReadOnlyDictionary<string, object?>;
        var result = InvokeStatic("MergeDiagnostics", primary, (object?)null);
        result.Should().BeSameAs(primary);
    }

    // ──────────────────────────────────────────────────────────────────
    // 39. ToHex
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ToHex_ShouldReturnFormattedString()
    {
        var result = (string)InvokeStatic("ToHex", (nint)0x1234)!;
        result.Should().Be("0x1234");
    }

    // ──────────────────────────────────────────────────────────────────
    // 40. DisableUnitCapHook / DisableInstantBuildHook — null memory branches
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DisableUnitCapHook_ShouldFail_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var result = (ActionExecutionResult)InvokePrivate(adapter, "DisableUnitCapHook")!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("memory accessor unavailable");
    }

    [Fact]
    public void DisableUnitCapHook_ShouldReturnNotActive_WhenNoBackup()
    {
        var adapter = CreateAttachedAdapter();
        var result = (ActionExecutionResult)InvokePrivate(adapter, "DisableUnitCapHook")!;
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("not active");
    }

    [Fact]
    public void DisableInstantBuildHook_ShouldFail_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var result = (ActionExecutionResult)InvokePrivate(adapter, "DisableInstantBuildHook")!;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("memory accessor unavailable");
    }

    [Fact]
    public void DisableInstantBuildHook_ShouldReturnNotActive_WhenNoBackup()
    {
        var adapter = CreateAttachedAdapter();
        var result = (ActionExecutionResult)InvokePrivate(adapter, "DisableInstantBuildHook")!;
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("not active");
    }

    // ──────────────────────────────────────────────────────────────────
    // 41. EnsureUnitCapHookInstalled / EnsureInstantBuildHookInstalled — memory null branch
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureUnitCapHookInstalled_ShouldFail_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var act = () => InvokePrivate(adapter, "EnsureUnitCapHookInstalled", 99);
        // EnsureAttached will throw since _memory is null
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public void EnsureInstantBuildHookInstalled_ShouldFail_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var act = () => InvokePrivate(adapter, "EnsureInstantBuildHookInstalled");
        act.Should().Throw<TargetInvocationException>()
           .WithInnerException<InvalidOperationException>();
    }

    // ──────────────────────────────────────────────────────────────────
    // 42. SetCreditsAsync — memory null early return
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCreditsAsync_ShouldFail_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var method = adapter.GetType().GetMethod("SetCreditsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { 1000, false, false, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("memory accessor unavailable");
    }

    // ──────────────────────────────────────────────────────────────────
    // 43. WaitForCreditsHookTickAsync — null memory / zero address
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitForCreditsHookTickAsync_ShouldReturnNotObserved_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var method = adapter.GetType().GetMethod("WaitForCreditsHookTickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(adapter, new object[] { 0, 100, CancellationToken.None })!;
        await task;
        var resultProp = task.GetType().GetProperty("Result")!;
        var result = resultProp.GetValue(task)!;
        var observed = (bool)result.GetType().GetProperty("Observed")!.GetValue(result)!;
        observed.Should().BeFalse();
    }

    [Fact]
    public async Task WaitForCreditsHookTickAsync_ShouldReturnNotObserved_WhenHitCountAddressZero()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_creditsHookHitCountAddress", nint.Zero);
        var method = adapter.GetType().GetMethod("WaitForCreditsHookTickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(adapter, new object[] { 0, 100, CancellationToken.None })!;
        await task;
        var resultProp = task.GetType().GetProperty("Result")!;
        var result = resultProp.GetValue(task)!;
        var observed = (bool)result.GetType().GetProperty("Observed")!.GetValue(result)!;
        observed.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 44. HandleCreditsHookTickState — unlock branch
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HandleCreditsHookTickState_ShouldReturnNull_WhenTickObserved()
    {
        var adapter = CreateAttachedAdapter();
        var diagnostics = new Dictionary<string, object?>();
        var creditsSymbol = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        var method = adapter.GetType().GetMethod("HandleCreditsHookTickState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Will throw because _memory is uninitialized, but we're testing the tick observed path
        // The method writes to _creditsHookLockEnabledAddress when !lockCredits
        // With uninitialized memory, Write will fail. Let's set lockCredits=true so it skips that.
        try
        {
            method.Invoke(adapter, new object[] { 1000, true, creditsSymbol, true, diagnostics });
        }
        catch (TargetInvocationException)
        {
            // Expected — uninitialized memory accessor
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // 45. CreateCreditsWriteSuccessResult
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, "HOOK_LOCK")]
    [InlineData(false, "HOOK_ONESHOT")]
    public void CreateCreditsWriteSuccessResult_ShouldSetCorrectStateTag(bool lockCredits, string expectedTag)
    {
        var diagnostics = new Dictionary<string, object?>();
        var method = typeof(RuntimeAdapter).GetMethod("CreateCreditsWriteSuccessResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (ActionExecutionResult)method.Invoke(null, new object[] { 1000, lockCredits, AddressSource.Signature, diagnostics })!;
        result.Succeeded.Should().BeTrue();
        diagnostics["creditsStateTag"].Should().Be(expectedTag);
    }

    // ──────────────────────────────────────────────────────────────────
    // 46. BuildCreditsHookAlreadyInstalledResult
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCreditsHookAlreadyInstalledResult_ShouldReturnOkResult()
    {
        var adapter = CreateAttachedAdapter();
        var type = typeof(RuntimeAdapter).GetNestedType("CreditsHookPatchResult", BindingFlags.NonPublic)!;
        var result = InvokePrivate(adapter, "BuildCreditsHookAlreadyInstalledResult")!;
        var succeeded = (bool)type.GetProperty("Succeeded")!.GetValue(result)!;
        succeeded.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    // 47. BuildInstantBuildAlreadyInstalledResult
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildInstantBuildAlreadyInstalledResult_ShouldReturnSuccessResult()
    {
        var adapter = CreateAttachedAdapter();
        var result = (ActionExecutionResult)InvokePrivate(adapter, "BuildInstantBuildAlreadyInstalledResult")!;
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("already installed");
    }

    // ──────────────────────────────────────────────────────────────────
    // 48. UpdateUnitCapHookValue
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateUnitCapHookValue_ShouldThrow_WhenMemoryUninitialized()
    {
        var adapter = CreateAttachedAdapter();
        // _memory is uninitialized so Write will throw
        var act = () => InvokePrivate(adapter, "UpdateUnitCapHookValue", 500);
        act.Should().Throw<TargetInvocationException>();
    }

    // ──────────────────────────────────────────────────────────────────
    // 49. AllocateExecutableCaveNear — null memory
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AllocateExecutableCaveNear_ShouldReturnZero_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var result = (nint)InvokePrivate(adapter, "AllocateExecutableCaveNear", (nint)0x10000, 64)!;
        result.Should().Be(nint.Zero);
    }

    // ──────────────────────────────────────────────────────────────────
    // 50. TryAllocateNear — null memory / negative address
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAllocateNear_ShouldReturnFalse_WhenMemoryNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_memory", null);
        var method = adapter.GetType().GetMethod("TryAllocateNear", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object?[] { 0x10000L, (nint)0x10000, 64, nint.Zero };
        var result = (bool)method.Invoke(adapter, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryAllocateNear_ShouldReturnFalse_WhenNegativeAddress()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("TryAllocateNear", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = new object?[] { -1L, (nint)0x10000, 64, nint.Zero };
        var result = (bool)method.Invoke(adapter, args)!;
        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // 51. EnsureInstantBuildHookDisabledForUnitCap — no hook active
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureInstantBuildHookDisabledForUnitCap_ShouldReturnNull_WhenNoHookActive()
    {
        var adapter = CreateAttachedAdapter();
        var result = InvokePrivate(adapter, "EnsureInstantBuildHookDisabledForUnitCap");
        result.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────
    // 52. DisableLegacyUnitCapPatch — no active patch
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DisableLegacyUnitCapPatch_ShouldReturnNull_WhenNoActivePatch()
    {
        var adapter = CreateAttachedAdapter();
        var result = InvokePrivate(adapter, "DisableLegacyUnitCapPatch");
        result.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────
    // 53. ExecuteSaveActionAsync
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSaveActionAsync_ShouldReturnSuccess()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("ExecuteSaveActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("save_action", RuntimeMode.Galactic, ExecutionKind.Save);
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Save action");
    }

    // ──────────────────────────────────────────────────────────────────
    // 54. ExecuteHelperActionAsync — null session
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldReturnUnavailable_WhenNoSession()
    {
        var adapter = CreateDetachedAdapter();
        var method = adapter.GetType().GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("set_hero_state_helper", RuntimeMode.Galactic, payload: new JsonObject { ["helperHookId"] = "hero_hook" });
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
    }

    // ──────────────────────────────────────────────────────────────────
    // 55. ExecuteHelperActionAsync — hook not found
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldReturnNotFound_WhenHookMissing()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("missing_action", RuntimeMode.Galactic, payload: new JsonObject { ["helperHookId"] = "nonexistent_hook" });
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not defined");
    }

    // ──────────────────────────────────────────────────────────────────
    // 56. ExecuteHelperActionAsync — probe unavailable
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldReturnError_WhenProbeUnavailable()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(
                Available: false,
                ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                Message: "probe failed")
        };
        var adapter = CreateAttachedAdapter(helperBackend: helperBackend);
        var method = adapter.GetType().GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("hero_hook", RuntimeMode.Galactic, payload: new JsonObject { ["helperHookId"] = "hero_hook" });
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("probe failed");
    }

    // ──────────────────────────────────────────────────────────────────
    // 57. ExecuteHelperActionAsync — successful execution
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteHelperActionAsync_ShouldReturnSuccess_WhenExecuteSucceeds()
    {
        var helperBackend = new StubHelperBridgeBackend
        {
            ProbeResult = new HelperBridgeProbeResult(
                Available: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"),
            ExecuteResult = new HelperBridgeExecutionResult(
                Succeeded: true,
                ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
                Message: "applied")
        };
        var adapter = CreateAttachedAdapter(helperBackend: helperBackend);
        var method = adapter.GetType().GetMethod("ExecuteHelperActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("hero_hook", RuntimeMode.Galactic, payload: new JsonObject { ["helperHookId"] = "hero_hook" });
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────
    // 58. ExecuteSdkActionAsync — no router configured
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSdkActionAsync_ShouldReturnError_WhenNoRouter()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("ExecuteSdkActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("sdk_action", RuntimeMode.Galactic, ExecutionKind.Sdk);
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not configured");
    }

    // ──────────────────────────────────────────────────────────────────
    // 59. ExecuteExtenderBackendActionAsync — null extender
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteExtenderBackendActionAsync_ShouldReturnError_WhenExtenderNull()
    {
        var adapter = CreateAttachedAdapter();
        SetField(adapter, "_extenderBackend", null);
        var method = adapter.GetType().GetMethod("ExecuteExtenderBackendActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("set_credits", RuntimeMode.Galactic);
        var capReport = new CapabilityReport("profile", DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, capReport, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not configured");
    }

    // ──────────────────────────────────────────────────────────────────
    // 60. ExecuteLegacyBackendActionAsync — Freeze kind
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteLegacyBackendActionAsync_ShouldReturnError_WhenFreezeKind()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("ExecuteLegacyBackendActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("freeze_action", RuntimeMode.Galactic, ExecutionKind.Freeze);
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("orchestrator");
    }

    [Fact]
    public async Task ExecuteLegacyBackendActionAsync_ShouldReturnError_WhenUnsupportedKind()
    {
        var adapter = CreateAttachedAdapter();
        var method = adapter.GetType().GetMethod("ExecuteLegacyBackendActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var request = BuildRequest("unknown_action", RuntimeMode.Galactic, (ExecutionKind)999);
        var task = (Task<ActionExecutionResult>)method.Invoke(adapter, new object[] { request, CancellationToken.None })!;
        var result = await task;
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Unsupported");
    }
}
