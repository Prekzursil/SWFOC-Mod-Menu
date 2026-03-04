#pragma warning disable CA1014
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeAdapterHookPrimitiveCoverageTests
{
    private static readonly Type RuntimeAdapterType = typeof(RuntimeAdapter);

    [Fact]
    public void HookByteBuilders_ShouldGenerateExpectedFrames()
    {
        var unitCapSize = ReadConstInt("UnitCapHookCaveSize");
        var instantBuildCaveSize = ReadConstInt("InstantBuildHookCaveSize");
        var creditsCaveSize = ReadConstInt("CreditsHookCaveSize");

        var unitCapBytes = (byte[])InvokePrivateStatic("BuildUnitCapHookCaveBytes", (nint)0x100000, (nint)0x200000, 250)!;
        unitCapBytes.Length.Should().Be(unitCapSize);
        unitCapBytes[0].Should().Be(0xBF);

        var originalInstantInstruction = new byte[] { 0x8B, 0x83, 0x04, 0x09, 0x00, 0x00 };
        var instantBuildBytes = (byte[])InvokePrivateStatic("BuildInstantBuildHookCaveBytes", (nint)0x300000, (nint)0x400000, originalInstantInstruction)!;
        instantBuildBytes.Length.Should().Be(instantBuildCaveSize);

        var jumpPatch = (byte[])InvokePrivateStatic("BuildInstantBuildJumpPatchBytes", (nint)0x401000, (nint)0x402000)!;
        jumpPatch.Length.Should().Be(6);
        jumpPatch[^1].Should().Be(0x90);

        var originalCreditsInstruction = new byte[] { 0xF3, 0x0F, 0x2C, 0x40, 0x58 };
        var creditsBytes = (byte[])InvokePrivateStatic(
            "BuildCreditsHookCaveBytes",
            (nint)0x500000,
            (nint)0x600000,
            originalCreditsInstruction,
            (byte)0x58,
            (byte)2)!;
        creditsBytes.Length.Should().Be(creditsCaveSize);

        var relJump = (byte[])InvokePrivateStatic("BuildRelativeJumpBytes", (nint)0x700000, (nint)0x700200)!;
        relJump[0].Should().Be(0xE9);

        var destination = new byte[8];
        InvokePrivateStatic("WriteInt32", destination, 2, 0x44332211);
        destination[2].Should().Be(0x11);
        destination[3].Should().Be(0x22);
        destination[4].Should().Be(0x33);
        destination[5].Should().Be(0x44);
    }

    [Fact]
    public void HookInputValidators_ShouldRejectInvalidInputs()
    {
        var badLength = () => InvokePrivateStatic(
            "BuildCreditsHookCaveBytes",
            (nint)0x1000,
            (nint)0x2000,
            new byte[] { 0xF3, 0x0F, 0x2C, 0x40 },
            (byte)0x58,
            (byte)1);
        badLength.Should().Throw<TargetInvocationException>();

        var badRegister = () => InvokePrivateStatic(
            "BuildCreditsHookCaveBytes",
            (nint)0x1000,
            (nint)0x2000,
            new byte[] { 0xF3, 0x0F, 0x2C, 0x40, 0x58 },
            (byte)0x58,
            (byte)9);
        badRegister.Should().Throw<TargetInvocationException>();

        var farTarget = new IntPtr(long.MaxValue);
        var displacementOverflow = () => InvokePrivateStatic(
            "ComputeRelativeDisplacement",
            IntPtr.Zero,
            farTarget);
        displacementOverflow.Should().Throw<TargetInvocationException>();
    }

    [Fact]
    public void PatternHelpers_ShouldHandleValidAndInvalidCandidates()
    {
        var instruction = new byte[] { 0xF3, 0x0F, 0x2C, 0x40, 0x58, 0x90, 0x90 };
        var parseArgs = new object?[] { instruction, 0, null };
        var parsed = (bool)InvokePrivateStatic("TryParseCreditsCvttss2siInstruction", parseArgs)!;
        parsed.Should().BeTrue();
        parseArgs[2].Should().NotBeNull();

        var badPrefixArgs = new object?[] { new byte[] { 0xF2, 0x0F, 0x2C, 0x40, 0x58 }, 0, null };
        ((bool)InvokePrivateStatic("TryParseCreditsCvttss2siInstruction", badPrefixArgs)!).Should().BeFalse();

        var aobPatternType = RuntimeAdapterType.Assembly.GetType("SwfocTrainer.Runtime.Scanning.AobPattern");
        aobPatternType.Should().NotBeNull();
        var parsePattern = aobPatternType!.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static);
        parsePattern.Should().NotBeNull();
        var pattern = parsePattern!.Invoke(null, new object[] { "F3 0F 2C ?? 58" });

        var hits = InvokePrivateStatic("FindPatternOffsets", instruction, pattern!, 10);
        var offsets = ((System.Collections.IEnumerable)hits!).Cast<object>().Select(v => (int)v).ToArray();
        offsets.Should().Contain(0);

        var falseMatch = (bool)InvokePrivateStatic("IsPatternMatchAtOffset", instruction, new byte?[] { 0x90, 0x90 }, 0)!;
        falseMatch.Should().BeFalse();

        var trueMatch = (bool)InvokePrivateStatic("IsPatternMatchAtOffset", instruction, new byte?[] { 0xF3, 0x0F }, 0)!;
        trueMatch.Should().BeTrue();

        var module = new byte[64];
        module[8] = 0x89;
        module[9] = 0x05;
        var creditsRva = 0x30L;
        var disp = (int)(creditsRva - (8 + 6));
        BitConverter.GetBytes(disp).CopyTo(module, 10);
        var hasNearby = (bool)InvokePrivateStatic("HasNearbyStoreToCreditsRva", module, 0, 32, creditsRva)!;
        hasNearby.Should().BeTrue();

        var immediateStore = (bool)InvokePrivateStatic(
            "LooksLikeImmediateStoreFromConvertedRegister",
            new byte[] { 0x89, 0x18 },
            0,
            (byte)3)!;
        immediateStore.Should().BeTrue();

        var wrongRegister = (bool)InvokePrivateStatic(
            "LooksLikeImmediateStoreFromConvertedRegister",
            new byte[] { 0x89, 0x10 },
            0,
            (byte)1)!;
        wrongRegister.Should().BeFalse();
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = RuntimeAdapterType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected private static method '{methodName}'.");

        return args.Length == 1 && args[0] is object?[] argumentArray
            ? method!.Invoke(null, argumentArray)
            : method!.Invoke(null, args);
    }

    private static int ReadConstInt(string fieldName)
    {
        var field = RuntimeAdapterType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull($"Expected private constant field '{fieldName}'.");
        return (int)field!.GetRawConstantValue()!;
    }
}
#pragma warning restore CA1014
