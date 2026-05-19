using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

/// <summary>
/// Wave 11 coverage: targets remaining uncovered lines and partial branches in
/// GitHubProfileUpdateService, ModOnboardingService, FileSystemProfileRepository,
/// and GitHubProfileUpdateExtractionHelpers.
/// </summary>
public sealed class ProfilesWave11CoverageTests
{
    #region ModOnboardingService — ResolveSeedDraftProfileId branches (L464-482)

    [Fact]
    public void ResolveSeedDraftProfileId_WithDraftProfileId_ReturnsDraftProfileId()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDraftProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var seed = CreateSeed(draftProfileId: "my_draft");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("my_draft");
    }

    [Fact]
    public void ResolveSeedDraftProfileId_NoDraftId_FallsBackToWorkshopId()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDraftProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(draftProfileId: "", workshopId: "12345");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("workshop_12345");
    }

    [Fact]
    public void ResolveSeedDraftProfileId_NoDraftIdNoWorkshopId_FallsBackToTitle()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDraftProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(draftProfileId: "", workshopId: "", title: "My Mod Title");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("My Mod Title");
    }

    [Fact]
    public void ResolveSeedDraftProfileId_AllEmpty_ReturnsNull()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDraftProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(draftProfileId: "", workshopId: "", title: "");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    #endregion

    #region ModOnboardingService — ResolveSeedDisplayName branches (L484-502)

    [Fact]
    public void ResolveSeedDisplayName_WithDisplayName_ReturnsDisplayName()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDisplayName",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var seed = CreateSeed(displayName: "My Display Name");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("My Display Name");
    }

    [Fact]
    public void ResolveSeedDisplayName_NoDisplayName_FallsBackToTitle()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDisplayName",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(displayName: "", title: "Mod Title");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("Mod Title");
    }

    [Fact]
    public void ResolveSeedDisplayName_NoDisplayNameNoTitle_FallsBackToWorkshopId()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDisplayName",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(displayName: "", title: "", workshopId: "99999");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("Workshop Mod 99999");
    }

    [Fact]
    public void ResolveSeedDisplayName_AllEmpty_ReturnsNull()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedDisplayName",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(displayName: "", title: "", workshopId: "");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    #endregion

    #region ModOnboardingService — ResolveSeedSourceRunId (L504-509)

    [Fact]
    public void ResolveSeedSourceRunId_WithValue_ReturnsTrimmed()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedSourceRunId",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var seed = CreateSeed(sourceRunId: "  run_123  ");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("run_123");
    }

    [Fact]
    public void ResolveSeedSourceRunId_Empty_ReturnsNull()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveSeedSourceRunId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(sourceRunId: "   ");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    #endregion

    #region ModOnboardingService — ResolveBaseProfileId branches (L511-529)

    [Fact]
    public void ResolveBaseProfileId_WithBaseProfileId_ReturnsBaseProfileId()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveBaseProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var seed = CreateSeed(baseProfileId: "  base_id  ");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("base_id");
    }

    [Fact]
    public void ResolveBaseProfileId_NoBaseId_FallsBackToCandidateBaseProfile()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveBaseProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(baseProfileId: "", candidateBaseProfile: "  candidate  ");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("candidate");
    }

    [Fact]
    public void ResolveBaseProfileId_NoBaseIdNoCandidate_FallsBackToParentProfile()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveBaseProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(baseProfileId: "", candidateBaseProfile: "", parentProfile: "  parent  ");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().Be("parent");
    }

    [Fact]
    public void ResolveBaseProfileId_AllEmpty_ReturnsNull()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveBaseProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(baseProfileId: "", candidateBaseProfile: "", parentProfile: "");
        var result = (string?)method!.Invoke(null, new object[] { seed });
        result.Should().BeNull();
    }

    #endregion

    #region ModOnboardingService — ResolveParentProfile branches (L531-544)

    [Fact]
    public void ResolveParentProfile_WithParentProfile_ReturnsParentProfile()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveParentProfile",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var seed = CreateSeed(parentProfile: "  parent_prof  ");
        var result = (string?)method!.Invoke(null, new object[] { seed, "fallback" });
        result.Should().Be("parent_prof");
    }

    [Fact]
    public void ResolveParentProfile_NoParent_FallsBackToCandidateBase()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveParentProfile",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(parentProfile: "", candidateBaseProfile: "  candidate_base  ");
        var result = (string?)method!.Invoke(null, new object[] { seed, "fallback" });
        result.Should().Be("candidate_base");
    }

    [Fact]
    public void ResolveParentProfile_AllEmpty_ReturnsFallback()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ResolveParentProfile",
            BindingFlags.NonPublic | BindingFlags.Static);

        var seed = CreateSeed(parentProfile: "", candidateBaseProfile: "");
        var result = (string?)method!.Invoke(null, new object[] { seed, "fallback_id" });
        result.Should().Be("fallback_id");
    }

    #endregion

    #region ModOnboardingService — NormalizeRiskLevel branches (L622-630)

    [Fact]
    public void NormalizeRiskLevel_Low_ReturnsLow()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeRiskLevel",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((string)method!.Invoke(null, new object?[] { "LOW" })!).Should().Be("low");
    }

    [Fact]
    public void NormalizeRiskLevel_High_ReturnsHigh()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeRiskLevel",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((string)method!.Invoke(null, new object?[] { "  HIGH  " })!).Should().Be("high");
    }

    [Fact]
    public void NormalizeRiskLevel_Medium_ReturnsMedium()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeRiskLevel",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((string)method!.Invoke(null, new object?[] { "medium" })!).Should().Be("medium");
    }

    [Fact]
    public void NormalizeRiskLevel_Unknown_ReturnsMedium()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeRiskLevel",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((string)method!.Invoke(null, new object?[] { "extreme" })!).Should().Be("medium");
    }

    [Fact]
    public void NormalizeRiskLevel_Null_ReturnsMedium()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeRiskLevel",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((string)method!.Invoke(null, new object?[] { null })!).Should().Be("medium");
    }

    [Fact]
    public void NormalizeRiskLevel_Whitespace_ReturnsMedium()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeRiskLevel",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((string)method!.Invoke(null, new object?[] { "   " })!).Should().Be("medium");
    }

    #endregion

    #region ModOnboardingService — ValidateSeedInputs branches (L250-278)

    [Fact]
    public void ValidateSeedInputs_AllEmpty_ReportsAllErrors()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ValidateSeedInputs",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // SeedValidationInput is a private struct, so we need to use the struct type
        var inputType = typeof(ModOnboardingService).GetNestedType("SeedValidationInput",
            BindingFlags.NonPublic);
        inputType.Should().NotBeNull();

        var input = Activator.CreateInstance(inputType!, new object?[]
        {
            "",           // DraftProfileId
            "",           // DisplayName
            "",           // SourceRunId
            double.NaN,   // Confidence (non-finite)
            ""            // BaseProfileId
        });

        var errors = new List<string>();
        method!.Invoke(null, new[] { input, errors });
        errors.Should().Contain(e => e.Contains("DraftProfileId"));
        errors.Should().Contain(e => e.Contains("DisplayName"));
        errors.Should().Contain(e => e.Contains("SourceRunId"));
        errors.Should().Contain(e => e.Contains("Confidence"));
        errors.Should().Contain(e => e.Contains("BaseProfileId"));
    }

    [Fact]
    public void ValidateSeedInputs_ValidInputs_ReportsNoErrors()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "ValidateSeedInputs",
            BindingFlags.NonPublic | BindingFlags.Static);

        var inputType = typeof(ModOnboardingService).GetNestedType("SeedValidationInput",
            BindingFlags.NonPublic);

        var input = Activator.CreateInstance(inputType!, new object?[]
        {
            "draft_id",
            "Display Name",
            "run_001",
            0.85,
            "base_profile"
        });

        var errors = new List<string>();
        method!.Invoke(null, new[] { input, errors });
        errors.Should().BeEmpty();
    }

    #endregion

    #region ModOnboardingService — NormalizeProfileId empty throws (L451-453)

    [Fact]
    public void NormalizeProfileId_Whitespace_ThrowsInvalidDataException()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var act = () => method!.Invoke(null, new object[] { "   " });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidDataException>()
            .WithMessage("*normalized to empty*");
    }

    [Fact]
    public void NormalizeProfileId_SpecialCharsOnly_ThrowsInvalidDataException()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var act = () => method!.Invoke(null, new object[] { "!!@@##" });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidDataException>();
    }

    #endregion

    #region ModOnboardingService — NormalizeProfileId custom_ prefix (L456-459)

    [Fact]
    public void NormalizeProfileId_AlreadyHasCustomPrefix_DoesNotDoublePrefix()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeProfileId",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (string)method!.Invoke(null, new object[] { "custom_my_mod" })!;
        result.Should().Be("custom_my_mod");
        result.Should().NotStartWith("custom_custom_");
    }

    #endregion

    #region ModOnboardingService — MergeWorkshopIds branches (L546-571)

    [Fact]
    public void MergeWorkshopIds_AllInputs_DeduplicatesAndSorts()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "MergeWorkshopIds",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[]
        {
            "111",                                         // workshopId
            new[] { "222", " 111 ", "" },                  // declared
            new[] { "333", " 222 " }                       // inferred
        })!;

        result.Should().HaveCount(3);
        result.Should().Contain("111");
        result.Should().Contain("222");
        result.Should().Contain("333");
    }

    [Fact]
    public void MergeWorkshopIds_NullDeclared_StillWorks()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "MergeWorkshopIds",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[]
        {
            null,                          // workshopId
            null,                          // declared (null)
            new[] { "444" }                // inferred
        })!;

        result.Should().Contain("444");
    }

    #endregion

    #region ModOnboardingService — InferAliases with user aliases (L720-763)

    [Fact]
    public void InferAliases_WithUserAliases_IncludesAll()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "InferAliases",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[]
        {
            "custom_mod",
            "My Mod",
            new[] { "alias1", "  ", "", "Alias2" }
        })!;

        result.Should().Contain("custom_mod");
        result.Should().Contain("my_mod");
        result.Should().Contain("alias1");
        result.Should().Contain("alias2");
    }

    [Fact]
    public void InferAliases_NullUserAliases_StillWorks()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "InferAliases",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[]
        {
            "custom_mod",
            "My Mod",
            null
        })!;

        result.Should().Contain("custom_mod");
        result.Should().Contain("my_mod");
    }

    #endregion

    #region ModOnboardingService — IsPathHintCandidate reserved token (L692)

    [Fact]
    public void IsPathHintCandidate_ReservedToken_ReturnsFalse()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "IsPathHintCandidate",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((bool)method!.Invoke(null, new object[] { "steamapps" })!).Should().BeFalse();
        ((bool)method!.Invoke(null, new object[] { "gamedata" })!).Should().BeFalse();
        ((bool)method!.Invoke(null, new object[] { "mods" })!).Should().BeFalse();
    }

    #endregion

    #region ModOnboardingService — AddFilteredListMetadata branches (L403-409)

    [Fact]
    public void AddFilteredListMetadata_NullList_DoesNotAdd()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddFilteredListMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { metadata, "key", null });
        metadata.Should().NotContainKey("key");
    }

    [Fact]
    public void AddFilteredListMetadata_EmptyList_DoesNotAdd()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddFilteredListMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { metadata, "key", Array.Empty<string>() });
        metadata.Should().NotContainKey("key");
    }

    [Fact]
    public void AddFilteredListMetadata_WithValues_FiltersWhitespace()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddFilteredListMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { metadata, "key", new[] { "a", "  ", "b" } });
        metadata.Should().ContainKey("key");
        metadata["key"].Should().Be("a,b");
    }

    #endregion

    #region ModOnboardingService — AddTrimmedMetadata branches (L395-401)

    [Fact]
    public void AddTrimmedMetadata_NullValue_DoesNotAdd()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddTrimmedMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { metadata, "key", null });
        metadata.Should().NotContainKey("key");
    }

    [Fact]
    public void AddTrimmedMetadata_WhitespaceValue_DoesNotAdd()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddTrimmedMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { metadata, "key", "   " });
        metadata.Should().NotContainKey("key");
    }

    [Fact]
    public void AddTrimmedMetadata_ValidValue_AddsTrimmed()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddTrimmedMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object?[] { metadata, "key", "  value  " });
        metadata["key"].Should().Be("value");
    }

    #endregion

    #region ModOnboardingService — AddMetadataIfNotEmpty branches (L387-393)

    [Fact]
    public void AddMetadataIfNotEmpty_EmptyList_DoesNotAdd()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddMetadataIfNotEmpty",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { metadata, "key", Array.Empty<string>() });
        metadata.Should().NotContainKey("key");
    }

    [Fact]
    public void AddMetadataIfNotEmpty_WithValues_Adds()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "AddMetadataIfNotEmpty",
            BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { metadata, "key", new[] { "a", "b" } });
        metadata["key"].Should().Be("a,b");
    }

    #endregion

    #region ModOnboardingService — TokenizeHintInput (L699-718)

    [Fact]
    public void TokenizeHintInput_WithSpecialChars_SplitsCorrectly()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "TokenizeHintInput",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = ((IEnumerable<string>)method!.Invoke(null, new object[] { @"C:\Games\Mods\MyMod-v1.2" })!).ToList();
        result.Should().NotBeEmpty();
        // Should contain tokens like "games", "mymod", "v1", etc.
    }

    #endregion

    #region ModOnboardingService — NormalizeNamespace special chars to underscore (L432-439)

    [Fact]
    public void NormalizeNamespace_SpecialChars_NormalizesToUnderscore()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeNamespace",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, new object?[] { "My-Namespace.v2" })!;
        result.Should().NotContain("-");
        result.Should().NotContain(".");
    }

    [Fact]
    public void NormalizeNamespace_OnlySpecialChars_ReturnsCustom()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "NormalizeNamespace",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (string)method!.Invoke(null, new object?[] { "---" })!;
        result.Should().Be("custom");
    }

    #endregion

    #region GitHubProfileUpdateExtractionHelpers — IsDriveQualifiedPath edge cases (L98-103)

    [Fact]
    public void IsDriveQualifiedPath_SingleChar_ReturnsFalse()
    {
        var method = typeof(GitHubProfileUpdateExtractionHelpers).GetMethod(
            "IsDriveQualifiedPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((bool)method!.Invoke(null, new object[] { "C" })!).Should().BeFalse();
    }

    [Fact]
    public void IsDriveQualifiedPath_NumberColon_ReturnsFalse()
    {
        var method = typeof(GitHubProfileUpdateExtractionHelpers).GetMethod(
            "IsDriveQualifiedPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((bool)method!.Invoke(null, new object[] { "1:" })!).Should().BeFalse();
    }

    [Fact]
    public void IsDriveQualifiedPath_Empty_ReturnsFalse()
    {
        var method = typeof(GitHubProfileUpdateExtractionHelpers).GetMethod(
            "IsDriveQualifiedPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        ((bool)method!.Invoke(null, new object[] { "" })!).Should().BeFalse();
    }

    #endregion

    #region GitHubProfileUpdateExtractionHelpers — ExtractToDirectorySafely null guards (L11-12)

    [Fact]
    public void ExtractToDirectorySafely_NullZipPath_Throws()
    {
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely(null!, "extractDir");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExtractToDirectorySafely_NullExtractDir_Throws()
    {
        var act = () => GitHubProfileUpdateExtractionHelpers.ExtractToDirectorySafely("zip.zip", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region FileSystemProfileRepository — MergeMetadata null branches (L174-197)

    [Fact]
    public void MergeMetadata_NullParent_ReturnsChildOnly()
    {
        var method = typeof(FileSystemProfileRepository).GetMethod(
            "MergeMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var child = new Dictionary<string, string> { ["key1"] = "val1" };
        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { null, child })!;
        result.Should().ContainKey("key1");
    }

    [Fact]
    public void MergeMetadata_NullChild_ReturnsParentOnly()
    {
        var method = typeof(FileSystemProfileRepository).GetMethod(
            "MergeMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        var parent = new Dictionary<string, string> { ["key1"] = "val1" };
        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { parent, null })!;
        result.Should().ContainKey("key1");
    }

    [Fact]
    public void MergeMetadata_BothNull_ReturnsEmpty()
    {
        var method = typeof(FileSystemProfileRepository).GetMethod(
            "MergeMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { null, null })!;
        result.Should().BeEmpty();
    }

    #endregion

    #region FileSystemProfileRepository — MergeDistinctNonEmpty branches (L165-172)

    [Fact]
    public void MergeDistinctNonEmpty_BothNull_ReturnsEmpty()
    {
        var method = typeof(FileSystemProfileRepository).GetMethod(
            "MergeDistinctNonEmpty",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object?[] { null, null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeDistinctNonEmpty_WithValues_Deduplicates()
    {
        var method = typeof(FileSystemProfileRepository).GetMethod(
            "MergeDistinctNonEmpty",
            BindingFlags.NonPublic | BindingFlags.Static);

        var parent = new[] { "a", "b", "" };
        var child = new[] { "b", "c", "  " };
        var result = (string[])method!.Invoke(null, new object[] { parent, child })!;
        result.Should().Contain("a");
        result.Should().Contain("b");
        result.Should().Contain("c");
        result.Should().NotContain("");
        result.Should().NotContain("  ");
    }

    #endregion

    #region FileSystemProfileRepository — LoadManifestAsync null deserialization (L38)

    [Fact]
    public async Task FileSystemProfileRepository_LoadManifest_NullDeserialization_Throws()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"profiles_w11_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Join(tempDir, "manifest.json"), "null");
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = async () => await repo.LoadManifestAsync(CancellationToken.None);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region FileSystemProfileRepository — LoadManifestAsync missing file (L32-33)

    [Fact]
    public async Task FileSystemProfileRepository_LoadManifest_MissingFile_Throws()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"profiles_w11b_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = async () => await repo.LoadManifestAsync(CancellationToken.None);
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region FileSystemProfileRepository — LoadProfileAsync null deserialization (L57-58)

    [Fact]
    public async Task FileSystemProfileRepository_LoadProfile_NullDeserialization_Throws()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"profiles_w11c_{Guid.NewGuid():N}");
        var profilesDir = Path.Join(tempDir, "profiles");
        Directory.CreateDirectory(profilesDir);
        try
        {
            await File.WriteAllTextAsync(Path.Join(profilesDir, "test_profile.json"), "null");
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = async () => await repo.LoadProfileAsync("test_profile", CancellationToken.None);
            await act.Should().ThrowAsync<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region FileSystemProfileRepository — LoadProfileAsync missing file (L52-54)

    [Fact]
    public async Task FileSystemProfileRepository_LoadProfile_MissingFile_Throws()
    {
        var tempDir = Path.Join(Path.GetTempPath(), $"profiles_w11d_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new ProfileRepositoryOptions { ProfilesRootPath = tempDir };
            var repo = new FileSystemProfileRepository(options);
            var act = async () => await repo.LoadProfileAsync("nonexistent", CancellationToken.None);
            await act.Should().ThrowAsync<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region FileSystemProfileRepository — constructor null guard

    [Fact]
    public void FileSystemProfileRepository_Constructor_NullOptions_Throws()
    {
        var act = () => new FileSystemProfileRepository(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ModOnboardingService — constructor null guards

    [Fact]
    public void ModOnboardingService_Constructor_NullProfiles_Throws()
    {
        var act = () => new ModOnboardingService(
            null!,
            new ProfileRepositoryOptions());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ModOnboardingService_Constructor_NullOptions_Throws()
    {
        var mockProfiles = new StubProfileRepository();
        var act = () => new ModOnboardingService(mockProfiles, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ModOnboardingService — ScaffoldDraftProfileAsync null guard

    [Fact]
    public async Task ScaffoldDraftProfileAsync_NullRequest_Throws()
    {
        var sut = new ModOnboardingService(new StubProfileRepository(), new ProfileRepositoryOptions());
        var act = async () => await sut.ScaffoldDraftProfileAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_EmptyDraftProfileId_Throws()
    {
        var sut = new ModOnboardingService(new StubProfileRepository(), new ProfileRepositoryOptions());
        var request = new ModOnboardingRequest("", "display", "base", new[] { new ModLaunchSample(null, null, "test") });
        var act = async () => await sut.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*DraftProfileId*");
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_EmptyDisplayName_Throws()
    {
        var sut = new ModOnboardingService(new StubProfileRepository(), new ProfileRepositoryOptions());
        var request = new ModOnboardingRequest("draft_id", "", "base", new[] { new ModLaunchSample(null, null, "test") });
        var act = async () => await sut.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*DisplayName*");
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_EmptyLaunchSamples_Throws()
    {
        var sut = new ModOnboardingService(new StubProfileRepository(), new ProfileRepositoryOptions());
        var request = new ModOnboardingRequest("draft_id", "display", "base", Array.Empty<ModLaunchSample>());
        var act = async () => await sut.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*launch sample*");
    }

    #endregion

    #region ModOnboardingService — ScaffoldDraftProfilesFromSeedsAsync null guard

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_NullRequest_Throws()
    {
        var sut = new ModOnboardingService(new StubProfileRepository(), new ProfileRepositoryOptions());
        var act = async () => await sut.ScaffoldDraftProfilesFromSeedsAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_EmptySeeds_Throws()
    {
        var sut = new ModOnboardingService(new StubProfileRepository(), new ProfileRepositoryOptions());
        var request = new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>());
        var act = async () => await sut.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*seed*");
    }

    #endregion

    #region ModOnboardingService — InferWorkshopIds and InferPathHints with MODPATH (L644-682)

    [Fact]
    public void InferWorkshopIds_WithSteamModMarkers_ExtractsIds()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "InferWorkshopIds",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var samples = new[]
        {
            new ModLaunchSample("swfoc.exe", null, "STEAMMOD=12345 STEAMMOD=67890"),
            new ModLaunchSample(null, null, null)
        };
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { samples })!;
        result.Should().Contain("12345");
        result.Should().Contain("67890");
    }

    [Fact]
    public void InferPathHints_WithModPath_ExtractsPathHints()
    {
        var method = typeof(ModOnboardingService).GetMethod(
            "InferPathHints",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var samples = new[]
        {
            new ModLaunchSample(null, @"C:\Games\SWFOC\swfoc.exe", @"MODPATH=""C:\Games\SWFOC\Mods\MyCustomMod""")
        };
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { samples })!;
        result.Should().NotBeEmpty();
        // Should contain tokens from the path
    }

    #endregion

    #region Helpers

    private static GeneratedProfileSeed CreateSeed(
        string draftProfileId = "draft",
        string displayName = "Display",
        string baseProfileId = "base",
        string sourceRunId = "run_001",
        double confidence = 0.9,
        string parentProfile = "parent",
        string workshopId = "",
        string title = "",
        string candidateBaseProfile = "")
    {
        return new GeneratedProfileSeed(
            DraftProfileId: draftProfileId,
            DisplayName: displayName,
            BaseProfileId: baseProfileId,
            LaunchSamples: Array.Empty<ModLaunchSample>(),
            SourceRunId: sourceRunId,
            Confidence: confidence,
            ParentProfile: parentProfile,
            WorkshopId: workshopId,
            Title: title,
            CandidateBaseProfile: candidateBaseProfile);
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateMinimalProfile(profileId));
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateMinimalProfile(profileId));
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static TrainerProfile CreateMinimalProfile(string profileId)
        {
            return new TrainerProfile(
                Id: profileId,
                DisplayName: $"Profile {profileId}",
                Inherits: null,
                ExeTarget: ExeTarget.Unknown,
                SteamWorkshopId: null,
                SignatureSets: Array.Empty<SignatureSet>(),
                FallbackOffsets: new Dictionary<string, long>(),
                Actions: new Dictionary<string, ActionSpec>(),
                FeatureFlags: new Dictionary<string, bool>(),
                CatalogSources: Array.Empty<CatalogSource>(),
                SaveSchemaId: "test_schema",
                HelperModHooks: Array.Empty<HelperHookSpec>(),
                Metadata: new Dictionary<string, string>());
        }
    }

    #endregion
}
