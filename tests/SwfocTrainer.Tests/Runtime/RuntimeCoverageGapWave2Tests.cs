using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeCoverageGapWave2Tests
{
    [Fact]
    public void RuntimeAdapter_PrivateNestedResolutionFactories_ShouldReportExpectedState()
    {
        var runtimeType = typeof(RuntimeAdapter);

        var writeAttemptType = runtimeType
            .GetNestedType("WriteAttemptResult`1", BindingFlags.NonPublic)!
            .MakeGenericType(typeof(int));
        var successWithoutObservation = writeAttemptType
            .GetMethod("SuccessWithoutObservation", BindingFlags.Public | BindingFlags.Static)!;
        var successWithObservation = writeAttemptType
            .GetMethod("SuccessWithObservation", BindingFlags.Public | BindingFlags.Static)!;

        var unobserved = successWithoutObservation.Invoke(null, Array.Empty<object?>())!;
        GetProperty<bool>(unobserved, "Success").Should().BeTrue();
        GetProperty<bool>(unobserved, "HasObservedValue").Should().BeFalse();
        GetProperty<string>(unobserved, "ReasonCode").Should().Be("ok");

        var observed = successWithObservation.Invoke(null, [42])!;
        GetProperty<bool>(observed, "Success").Should().BeTrue();
        GetProperty<bool>(observed, "HasObservedValue").Should().BeTrue();
        GetProperty<int>(observed, "ObservedValue").Should().Be(42);

        var unitCapType = runtimeType.GetNestedType("UnitCapHookResolution", BindingFlags.NonPublic)!;
        var unitCapOk = unitCapType.GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [(nint)0x1111])!;
        var unitCapFail = unitCapType.GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, ["missing"])!;
        GetProperty<bool>(unitCapOk, "Succeeded").Should().BeTrue();
        GetProperty<nint>(unitCapOk, "Address").Should().Be((nint)0x1111);
        GetProperty<bool>(unitCapFail, "Succeeded").Should().BeFalse();
        GetProperty<string>(unitCapFail, "Message").Should().Be("missing");

        var instantBuildType = runtimeType.GetNestedType("InstantBuildHookResolution", BindingFlags.NonPublic)!;
        var instantOk = instantBuildType
            .GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [(nint)0x2222, new byte[] { 0x90, 0x91 }])!;
        var instantFail = instantBuildType
            .GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, ["instant-missing"])!;
        GetProperty<bool>(instantOk, "Succeeded").Should().BeTrue();
        GetProperty<nint>(instantOk, "Address").Should().Be((nint)0x2222);
        GetProperty<byte[]>(instantOk, "OriginalBytes").Should().Equal(0x90, 0x91);
        GetProperty<bool>(instantFail, "Succeeded").Should().BeFalse();
        GetProperty<byte[]>(instantFail, "OriginalBytes").Should().BeEmpty();

        var creditsType = runtimeType.GetNestedType("CreditsHookResolution", BindingFlags.NonPublic)!;
        var creditsOk = creditsType
            .GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [(nint)0x3333, (byte)0x70, (byte)0x02, new byte[] { 0xF3, 0x0F }])!;
        var creditsFail = creditsType
            .GetMethod("Fail", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, ["credits-missing"])!;
        GetProperty<bool>(creditsOk, "Succeeded").Should().BeTrue();
        GetProperty<nint>(creditsOk, "Address").Should().Be((nint)0x3333);
        GetProperty<byte>(creditsOk, "DetectedOffset").Should().Be(0x70);
        GetProperty<byte>(creditsOk, "DestinationReg").Should().Be(0x02);
        GetProperty<byte[]?>(creditsOk, "OriginalInstruction").Should().Equal(0xF3, 0x0F);
        GetProperty<bool>(creditsFail, "Succeeded").Should().BeFalse();
        GetProperty<string>(creditsFail, "Message").Should().Be("credits-missing");
    }

    [Fact]
    public async Task SignatureResolver_ShouldFallbackToExecutableName_WhenProcessIdLookupFails()
    {
        using var process = Process.GetCurrentProcess();
        var executablePath = process.MainModule?.FileName;
        executablePath.Should().NotBeNullOrWhiteSpace();

        var resolver = new SignatureResolver(
            NullLogger<SignatureResolver>.Instance,
            ghidraSymbolPackRoot: Path.GetTempPath());
        var build = new ProfileBuild(
            ProfileId: "runtime-gap",
            GameBuild: "test",
            ExecutablePath: executablePath!,
            ExeTarget: ExeTarget.Swfoc,
            ProcessId: int.MaxValue);

        var map = await resolver.ResolveAsync(
            build,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            CancellationToken.None);

        map.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void ModDependencyValidator_ShouldPass_WhenNoDependenciesDeclared()
    {
        var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var process = CreateProcess(Path.Combine(Path.GetTempPath(), "runtime-gap-no-deps"), commandLine: "StarWarsG.exe");

        var result = new ModDependencyValidator().Validate(profile, process);

        result.Status.Should().Be(DependencyValidationStatus.Pass);
        result.DisabledActionIds.Should().BeEmpty();
        result.Message.Should().Contain("No workshop dependencies declared");
    }

    [Fact]
    public void ModDependencyValidator_ShouldResolveRelativeModPathAndParentHints()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-runtime-gap-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var processDir = Path.Combine(tempRoot, "game", "corruption");
            var modRoot = Path.Combine(processDir, "roe-submod");
            var parentRoot = Path.Combine(processDir, "aotr-parent");
            WriteMarker(modRoot);
            WriteMarker(parentRoot);

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111,2222222222",
                ["requiredMarkerFile"] = "Data/XML/Gameobjectfiles.xml",
                ["dependencySensitiveActions"] = "spawn_unit_helper",
                ["localParentPathHints"] = "aotr-parent,../unsafe-parent"
            });

            var process = CreateProcess(modRoot, commandLine: "StarWarsG.exe MODPATH=roe-submod");
            var result = new ModDependencyValidator().Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.Pass);
            result.DisabledActionIds.Should().BeEmpty();
            result.Message.Should().Contain("Dependencies verified");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ModDependencyValidator_ShouldSoftFail_WhenRelativeModPathExistsButMarkerMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-runtime-gap-validator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var processDir = Path.Combine(tempRoot, "game", "corruption");
            var modRoot = Path.Combine(processDir, "broken-submod");
            Directory.CreateDirectory(modRoot);

            var profile = CreateProfile(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["requiredWorkshopIds"] = "1111111111",
                ["requiredMarkerFile"] = "Data/XML/Gameobjectfiles.xml",
                ["dependencySensitiveActions"] = "spawn_unit_helper"
            });

            var process = CreateProcess(modRoot, commandLine: "StarWarsG.exe MODPATH=broken-submod");
            var result = new ModDependencyValidator().Validate(profile, process);

            result.Status.Should().Be(DependencyValidationStatus.SoftFail);
            result.DisabledActionIds.Should().Contain("spawn_unit_helper");
            result.Message.Should().Contain("missing dependencies");
            result.Message.Should().Contain("Attach will continue");
            result.Message.Should().NotContain("Dependencies verified");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ValueFreezeService_ShouldKeepEntry_WhenPulseWriteFails()
    {
        var runtime = new ThrowingRuntimeAdapterStub(isAttached: true, failFirstSymbol: "credits");
        using var service = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 250);
        service.FreezeInt("credits", 100);

        var pulseCallback = typeof(ValueFreezeService)
            .GetMethod("PulseCallback", BindingFlags.Instance | BindingFlags.NonPublic);
        pulseCallback.Should().NotBeNull();

        pulseCallback!.Invoke(service, [null]);
        await Task.Delay(50);

        service.IsFrozen("credits").Should().BeTrue();
        runtime.AttemptedWrites.Should().Contain("credits");
        runtime.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ValueFreezeService_ShouldAggressivelyWrite_WhenAttached()
    {
        var runtime = new ThrowingRuntimeAdapterStub(isAttached: true);
        using var service = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 250);
        service.FreezeIntAggressive("credits", 9999);

        await WaitUntilAsync(() => runtime.Writes.ContainsKey("credits"), TimeSpan.FromSeconds(2));

        runtime.Writes["credits"].Should().Be(9999);
        service.Unfreeze("credits").Should().BeTrue();
        service.IsFrozen("credits").Should().BeFalse();
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        return (T)instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;
    }

    private static TrainerProfile CreateProfile(IReadOnlyDictionary<string, string> metadata)
    {
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["spawn_unit_helper"] = new ActionSpec(
                "spawn_unit_helper",
                ActionCategory.Unit,
                RuntimeMode.Unknown,
                ExecutionKind.Helper,
                new JsonObject(),
                VerifyReadback: false,
                CooldownMs: 0)
        };

        return new TrainerProfile(
            Id: "runtime_gap_profile",
            DisplayName: "Runtime gap profile",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema_v1",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static ProcessMetadata CreateProcess(string modPath, string commandLine)
    {
        var processPath = Path.Combine(Path.GetDirectoryName(modPath) ?? modPath, "StarWarsG.exe");
        var process = new ProcessMetadata(
            456,
            "StarWarsG",
            processPath,
            commandLine,
            ExeTarget.Swfoc,
            RuntimeMode.Unknown,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["detectedVia"] = "runtime_gap_tests",
                ["isStarWarsG"] = "true"
            });

        var context = new LaunchContextResolver().Resolve(process, Array.Empty<TrainerProfile>());
        return process with { LaunchContext = context };
    }

    private static void WriteMarker(string root)
    {
        var markerPath = Path.Combine(root, "Data", "XML", "Gameobjectfiles.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, "<GameObjectFiles />");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup for tests.
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var startedAt = Stopwatch.StartNew();
        while (!predicate())
        {
            if (startedAt.Elapsed > timeout)
            {
                throw new TimeoutException("Predicate was not satisfied within the allotted timeout.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class ThrowingRuntimeAdapterStub : IRuntimeAdapter
    {
        private readonly string? _failFirstSymbol;
        private readonly HashSet<string> _failedSymbols = new(StringComparer.OrdinalIgnoreCase);

        public ThrowingRuntimeAdapterStub(bool isAttached, string? failFirstSymbol = null)
        {
            IsAttached = isAttached;
            _failFirstSymbol = failFirstSymbol;
        }

        public Dictionary<string, object?> Writes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> AttemptedWrites { get; } = [];

        public bool IsAttached { get; set; }

        public AttachSession? CurrentSession => null;

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = cancellationToken;
            AttemptedWrites.Add(symbol);

            if (!string.IsNullOrWhiteSpace(_failFirstSymbol) &&
                symbol.Equals(_failFirstSymbol, StringComparison.OrdinalIgnoreCase) &&
                _failedSymbols.Add(symbol))
            {
                return Task.FromException(new InvalidOperationException($"Injected failure for {symbol}."));
            }

            Writes[symbol] = value;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task DetachAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IsAttached = false;
            return Task.CompletedTask;
        }
    }
}
