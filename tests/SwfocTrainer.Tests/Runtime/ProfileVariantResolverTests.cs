using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProfileVariantResolverTests
{
    [Fact]
    public async Task ResolveAsync_ShouldReturnRequestedProfile_WhenNotUniversal()
    {
        var resolver = new ProfileVariantResolver(new LaunchContextResolver(), NullLogger<ProfileVariantResolver>.Instance);

        var result = await resolver.ResolveAsync(
            requestedProfileId: "base_swfoc",
            processes: Array.Empty<ProcessMetadata>(),
            cancellationToken: CancellationToken.None);

        result.ResolvedProfileId.Should().Be("base_swfoc");
        result.ReasonCode.Should().Be("explicit_profile_selection");
    }

    [Fact]
    public async Task ResolveAsync_ShouldPickRoe_WhenWorkshopMarkerPresent()
    {
        var resolver = CreateResolverWithDefaultProfiles();

        var process = new ProcessMetadata(
            ProcessId: 123,
            ProcessName: "StarWarsG",
            ProcessPath: "C:/Games/corruption/StarWarsG.exe",
            CommandLine: "StarWarsG.exe LANGUAGE=ENGLISH STEAMMOD=3447786229 STEAMMOD=1397421866",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Unknown,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["isStarWarsG"] = "true",
                ["steamModIdsDetected"] = "1397421866,3447786229",
                ["detectedVia"] = "cmdline_mod_markers"
            });

        var result = await resolver.ResolveAsync(
            requestedProfileId: "universal_auto",
            processes: new[] { process },
            cancellationToken: CancellationToken.None);

        result.ResolvedProfileId.Should().Be("roe_3447786229_swfoc");
        result.ReasonCode.Should().Be("steammod_exact_roe");
        result.Confidence.Should().Be(1.0d);
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallbackToBaseSweaw_WhenSweawProcessDetected()
    {
        var resolver = new ProfileVariantResolver(new LaunchContextResolver(), NullLogger<ProfileVariantResolver>.Instance);

        var process = new ProcessMetadata(
            ProcessId: 33,
            ProcessName: "sweaw",
            ProcessPath: "C:/Games/EmpireAtWar/sweaw.exe",
            CommandLine: "sweaw.exe",
            ExeTarget: ExeTarget.Sweaw,
            Mode: RuntimeMode.Unknown);

        var result = await resolver.ResolveAsync(
            requestedProfileId: "universal_auto",
            processes: new[] { process },
            cancellationToken: CancellationToken.None);

        result.ResolvedProfileId.Should().Be("base_sweaw");
        result.Confidence.Should().BeGreaterThan(0.5d);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnSafeDefault_WhenNoProcessDetected()
    {
        var resolver = new ProfileVariantResolver(new LaunchContextResolver(), NullLogger<ProfileVariantResolver>.Instance);

        var result = await resolver.ResolveAsync(
            requestedProfileId: "universal_auto",
            processes: Array.Empty<ProcessMetadata>(),
            cancellationToken: CancellationToken.None);

        result.ResolvedProfileId.Should().Be("base_swfoc");
        result.ReasonCode.Should().Be("no_process_detected");
        result.Confidence.Should().BeLessThan(0.5d);
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseProcessLocator_WhenProcessesNotProvided()
    {
        var process = BuildProcessWithRecommendation("base_swfoc", "from_locator", 0.88d);
        var locator = new StubProcessLocator(process);
        var resolver = new ProfileVariantResolver(
            launchContextResolver: new LaunchContextResolver(),
            logger: NullLogger<ProfileVariantResolver>.Instance,
            profileRepository: null,
            processLocator: locator,
            fingerprintService: null,
            capabilityMapResolver: null);

        var result = await resolver.ResolveAsync("universal_auto", cancellationToken: CancellationToken.None);

        locator.Called.Should().BeTrue();
        result.ResolvedProfileId.Should().Be("base_swfoc");
        result.ReasonCode.Should().Be("from_locator");
    }

    [Fact]
    public async Task ResolveAsync_ShouldUseFingerprintRecommendation_WhenLaunchRecommendationMissing()
    {
        var process = BuildProcessWithRecommendation(profileId: null, reasonCode: "none", confidence: 0.0d);
        var fingerprint = BuildFingerprint();
        var resolver = new ProfileVariantResolver(
            launchContextResolver: new LaunchContextResolver(),
            logger: NullLogger<ProfileVariantResolver>.Instance,
            profileRepository: null,
            processLocator: null,
            fingerprintService: new StubBinaryFingerprintService(fingerprint),
            capabilityMapResolver: new StubCapabilityMapResolver("base_sweaw"));

        var result = await resolver.ResolveAsync(
            requestedProfileId: "universal_auto",
            processes: new[] { process },
            cancellationToken: CancellationToken.None);

        result.ResolvedProfileId.Should().Be("base_sweaw");
        result.ReasonCode.Should().Be("fingerprint_default_profile");
        result.FingerprintId.Should().Be(fingerprint.FingerprintId);
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallbackToExeTarget_WhenFingerprintResolutionThrows()
    {
        var process = BuildProcessWithRecommendation(profileId: null, reasonCode: "none", confidence: 0.0d);
        var resolver = new ProfileVariantResolver(
            launchContextResolver: new LaunchContextResolver(),
            logger: NullLogger<ProfileVariantResolver>.Instance,
            profileRepository: new ThrowingProfileRepository(),
            processLocator: null,
            fingerprintService: new ThrowingBinaryFingerprintService(),
            capabilityMapResolver: new StubCapabilityMapResolver("base_swfoc"));

        var result = await resolver.ResolveAsync(
            requestedProfileId: "universal_auto",
            processes: new[] { process },
            cancellationToken: CancellationToken.None);

        result.ResolvedProfileId.Should().Be("base_swfoc");
        result.ReasonCode.Should().Be("exe_target_swfoc_fallback");
    }

    private static ProfileVariantResolver CreateResolverWithDefaultProfiles()
    {
        var root = TestPaths.FindRepoRoot();
        var repository = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        });

        return new ProfileVariantResolver(
            launchContextResolver: new LaunchContextResolver(),
            logger: NullLogger<ProfileVariantResolver>.Instance,
            profileRepository: repository,
            processLocator: null,
            fingerprintService: null,
            capabilityMapResolver: null);
    }

    private static ProcessMetadata BuildProcessWithRecommendation(string? profileId, string reasonCode, double confidence)
    {
        var recommendation = new ProfileRecommendation(profileId, reasonCode, confidence);
        var launchContext = new LaunchContext(
            LaunchKind.Workshop,
            CommandLineAvailable: true,
            SteamModIds: Array.Empty<string>(),
            ModPathRaw: null,
            ModPathNormalized: null,
            DetectedVia: "tests",
            Recommendation: recommendation);

        return new ProcessMetadata(
            ProcessId: 71,
            ProcessName: "StarWarsG",
            ProcessPath: "C:/Games/corruption/StarWarsG.exe",
            CommandLine: "StarWarsG.exe",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            LaunchContext: launchContext);
    }

    private static BinaryFingerprint BuildFingerprint()
    {
        return new BinaryFingerprint(
            FingerprintId: "fingerprint-1",
            FileSha256: "abc",
            ModuleName: "StarWarsG.exe",
            ProductVersion: "1.0",
            FileVersion: "1.0",
            TimestampUtc: DateTimeOffset.UtcNow,
            ModuleList: Array.Empty<string>(),
            SourcePath: "C:/Games/corruption/StarWarsG.exe");
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        private readonly ProcessMetadata _process;

        public StubProcessLocator(ProcessMetadata process)
        {
            _process = process;
        }

        public bool Called { get; private set; }

        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult<IReadOnlyList<ProcessMetadata>>(new[] { _process });
        }

        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            return Task.FromResult<ProcessMetadata?>(_process);
        }
    }

    private sealed class StubBinaryFingerprintService : IBinaryFingerprintService
    {
        private readonly BinaryFingerprint _fingerprint;

        public StubBinaryFingerprintService(BinaryFingerprint fingerprint)
        {
            _fingerprint = fingerprint;
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath)
        {
            _ = modulePath;
            return Task.FromResult(_fingerprint);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, CancellationToken cancellationToken)
        {
            _ = modulePath;
            _ = cancellationToken;
            return Task.FromResult(_fingerprint);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId)
        {
            _ = modulePath;
            _ = processId;
            return Task.FromResult(_fingerprint);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId, CancellationToken cancellationToken)
        {
            _ = modulePath;
            _ = processId;
            _ = cancellationToken;
            return Task.FromResult(_fingerprint);
        }
    }

    private sealed class ThrowingBinaryFingerprintService : IBinaryFingerprintService
    {
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath)
            => throw new InvalidOperationException(modulePath);

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new InvalidOperationException(modulePath);
        }

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId)
            => throw new InvalidOperationException($"{modulePath}:{processId}");

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"{modulePath}:{processId}:{cancellationToken.IsCancellationRequested}");
    }

    private sealed class StubCapabilityMapResolver : ICapabilityMapResolver
    {
        private readonly string _defaultProfileId;

        public StubCapabilityMapResolver(string defaultProfileId)
        {
            _defaultProfileId = defaultProfileId;
        }

        public Task<CapabilityResolutionResult> ResolveAsync(
            BinaryFingerprint fingerprint,
            string requestedProfileId,
            string operationId,
            IReadOnlySet<string> resolvedAnchors)
        {
            _ = requestedProfileId;
            _ = operationId;
            _ = resolvedAnchors;
            return Task.FromResult(new CapabilityResolutionResult(
                ProfileId: _defaultProfileId,
                OperationId: "op",
                State: SdkCapabilityStatus.Available,
                ReasonCode: CapabilityReasonCode.FingerprintDefaultProfile,
                Confidence: 1.0,
                FingerprintId: fingerprint.FingerprintId,
                MatchedAnchors: Array.Empty<string>(),
                MissingAnchors: Array.Empty<string>(),
                Metadata: CapabilityResolutionMetadata.Empty));
        }

        public Task<CapabilityResolutionResult> ResolveAsync(
            BinaryFingerprint fingerprint,
            string requestedProfileId,
            string operationId,
            IReadOnlySet<string> resolvedAnchors,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ResolveAsync(fingerprint, requestedProfileId, operationId, resolvedAnchors);
        }

        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint)
        {
            _ = fingerprint;
            return Task.FromResult<string?>(_defaultProfileId);
        }

        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return ResolveDefaultProfileIdAsync(fingerprint);
        }
    }

    private sealed class ThrowingProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException();

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            => throw new InvalidOperationException(profileId);

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            => throw new InvalidOperationException(profileId);

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException();
    }
}
