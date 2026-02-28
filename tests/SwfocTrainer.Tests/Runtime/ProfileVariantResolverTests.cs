using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
}
