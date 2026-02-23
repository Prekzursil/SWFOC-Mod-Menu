using System.IO;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Validation;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ProfileValidatorTests
{
    [Fact]
    public void Validate_ShouldThrow_WhenBackendPreferenceIsInvalid()
    {
        var profile = BuildProfile() with { BackendPreference = "banana" };

        var act = () => ProfileValidator.Validate(profile);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*backendPreference*");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenHostPreferenceIsInvalid()
    {
        var profile = BuildProfile() with { HostPreference = "launcher_only" };

        var act = () => ProfileValidator.Validate(profile);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*hostPreference*");
    }

    [Fact]
    public void Validate_ShouldPass_ForExtenderProfileContract()
    {
        var profile = BuildProfile() with
        {
            BackendPreference = "extender",
            HostPreference = "starwarsg_preferred",
            RequiredCapabilities = ["set_credits"]
        };

        var act = () => ProfileValidator.Validate(profile);

        act.Should().NotThrow();
    }

    private static TrainerProfile BuildProfile()
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: [
                new SignatureSet(
                    Name: "test",
                    GameBuild: "build",
                    Signatures: [
                        new SignatureSpec("credits", "AA BB", 0)
                    ])
            ],
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test_schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(),
            BackendPreference: "auto",
            RequiredCapabilities: Array.Empty<string>(),
            HostPreference: "any",
            ExperimentalFeatures: Array.Empty<string>());
    }
}
