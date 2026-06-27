using FluentAssertions;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Wave 2 coverage: fills remaining branches in ProfileMetadataParser,
/// FileAuditLogger, ActionSymbolRegistry, and other Core services with small gaps.
/// </summary>
public sealed class CoreWave2CoverageTests
{
    #region ProfileMetadataParser

    [Fact]
    public void ParseSymbolValidationRules_ShouldReturnEmpty_WhenMetadataIsNull()
    {
        var profile = BuildProfile(metadata: null);
        var rules = ProfileMetadataParser.ParseSymbolValidationRules(profile);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ParseSymbolValidationRules_ShouldReturnEmpty_WhenKeyIsMissing()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["other"] = "value" });
        var rules = ProfileMetadataParser.ParseSymbolValidationRules(profile);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ParseSymbolValidationRules_ShouldReturnEmpty_WhenValueIsWhitespace()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["symbolValidationRules"] = "   " });
        var rules = ProfileMetadataParser.ParseSymbolValidationRules(profile);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ParseSymbolValidationRules_ShouldReturnEmpty_WhenJsonIsInvalid()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["symbolValidationRules"] = "not-json{{{" });
        var rules = ProfileMetadataParser.ParseSymbolValidationRules(profile);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ParseSymbolValidationRules_ShouldReturnEmpty_WhenJsonDeserializesToNull()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["symbolValidationRules"] = "null" });
        var rules = ProfileMetadataParser.ParseSymbolValidationRules(profile);
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ParseSymbolValidationRules_ShouldParse_WhenJsonIsValid()
    {
        var json = """[{"symbol":"credits","mode":null,"intMin":0,"intMax":999999}]""";
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["symbolValidationRules"] = json });
        var rules = ProfileMetadataParser.ParseSymbolValidationRules(profile);
        rules.Should().ContainSingle();
        rules[0].Symbol.Should().Be("credits");
    }

    [Fact]
    public void ParseCriticalSymbolSet_ShouldReturnEmpty_WhenMetadataIsNull()
    {
        var profile = BuildProfile(metadata: null);
        var symbols = ProfileMetadataParser.ParseCriticalSymbolSet(profile);
        symbols.Should().BeEmpty();
    }

    [Fact]
    public void ParseCriticalSymbolSet_ShouldReturnEmpty_WhenKeyIsMissing()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["other"] = "value" });
        var symbols = ProfileMetadataParser.ParseCriticalSymbolSet(profile);
        symbols.Should().BeEmpty();
    }

    [Fact]
    public void ParseCriticalSymbolSet_ShouldReturnEmpty_WhenValueIsWhitespace()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["criticalSymbols"] = "   " });
        var symbols = ProfileMetadataParser.ParseCriticalSymbolSet(profile);
        symbols.Should().BeEmpty();
    }

    [Fact]
    public void ParseCriticalSymbolSet_ShouldSplitAndTrim()
    {
        var profile = BuildProfile(metadata: new Dictionary<string, string> { ["criticalSymbols"] = " credits , selected_hp ,  fog_reveal " });
        var symbols = ProfileMetadataParser.ParseCriticalSymbolSet(profile);
        symbols.Should().HaveCount(3);
        symbols.Should().Contain("credits");
        symbols.Should().Contain("selected_hp");
        symbols.Should().Contain("fog_reveal");
    }

    #endregion

    #region ActionSymbolRegistry

    [Fact]
    public void TryGetSymbol_ShouldResolve_KnownActions()
    {
        ActionSymbolRegistry.TryGetSymbol("set_credits", out var symbol).Should().BeTrue();
        symbol.Should().Be("credits");

        ActionSymbolRegistry.TryGetSymbol("freeze_timer", out symbol).Should().BeTrue();
        symbol.Should().Be("game_timer_freeze");

        ActionSymbolRegistry.TryGetSymbol("toggle_fog_reveal", out symbol).Should().BeTrue();
        symbol.Should().Be("fog_reveal");

        ActionSymbolRegistry.TryGetSymbol("toggle_ai", out symbol).Should().BeTrue();
        symbol.Should().Be("ai_enabled");

        ActionSymbolRegistry.TryGetSymbol("set_unit_cap", out symbol).Should().BeTrue();
        symbol.Should().Be("unit_cap");

        ActionSymbolRegistry.TryGetSymbol("set_selected_hp", out symbol).Should().BeTrue();
        symbol.Should().Be("selected_hp");

        ActionSymbolRegistry.TryGetSymbol("set_game_speed", out symbol).Should().BeTrue();
        symbol.Should().Be("game_speed");
    }

    [Fact]
    public void TryGetSymbol_ShouldReturnFalse_ForUnknownAction()
    {
        ActionSymbolRegistry.TryGetSymbol("unknown_action_xyz", out var symbol).Should().BeFalse();
        symbol.Should().BeEmpty();
    }

    [Fact]
    public void TryGetSymbol_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => ActionSymbolRegistry.TryGetSymbol(null!, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryGetSymbol_ShouldResolve_AllRemainingActions()
    {
        ActionSymbolRegistry.TryGetSymbol("read_symbol", out var s1).Should().BeTrue();
        s1.Should().Be("credits");

        ActionSymbolRegistry.TryGetSymbol("set_credits_extender_experimental", out var s2).Should().BeTrue();
        s2.Should().Be("credits");

        ActionSymbolRegistry.TryGetSymbol("set_instant_build_multiplier", out var s3).Should().BeTrue();
        s3.Should().Be("instant_build");

        ActionSymbolRegistry.TryGetSymbol("set_selected_shield", out var s4).Should().BeTrue();
        s4.Should().Be("selected_shield");

        ActionSymbolRegistry.TryGetSymbol("set_selected_speed", out var s5).Should().BeTrue();
        s5.Should().Be("selected_speed");

        ActionSymbolRegistry.TryGetSymbol("set_selected_damage_multiplier", out var s6).Should().BeTrue();
        s6.Should().Be("selected_damage_multiplier");

        ActionSymbolRegistry.TryGetSymbol("set_selected_cooldown_multiplier", out var s7).Should().BeTrue();
        s7.Should().Be("selected_cooldown_multiplier");

        ActionSymbolRegistry.TryGetSymbol("set_selected_veterancy", out var s8).Should().BeTrue();
        s8.Should().Be("selected_veterancy");

        ActionSymbolRegistry.TryGetSymbol("set_selected_owner_faction", out var s9).Should().BeTrue();
        s9.Should().Be("selected_owner_faction");

        ActionSymbolRegistry.TryGetSymbol("set_planet_owner", out var s10).Should().BeTrue();
        s10.Should().Be("planet_owner");

        ActionSymbolRegistry.TryGetSymbol("set_context_faction", out var s11).Should().BeTrue();
        s11.Should().Be("selected_owner_faction");

        ActionSymbolRegistry.TryGetSymbol("set_context_allegiance", out var s12).Should().BeTrue();
        s12.Should().Be("selected_owner_faction");

        ActionSymbolRegistry.TryGetSymbol("set_hero_respawn_timer", out var s13).Should().BeTrue();
        s13.Should().Be("hero_respawn_timer");

        ActionSymbolRegistry.TryGetSymbol("toggle_tactical_god_mode", out var s14).Should().BeTrue();
        s14.Should().Be("tactical_god_mode");

        ActionSymbolRegistry.TryGetSymbol("toggle_tactical_one_hit_mode", out var s15).Should().BeTrue();
        s15.Should().Be("tactical_one_hit_mode");

        ActionSymbolRegistry.TryGetSymbol("freeze_symbol", out var s16).Should().BeTrue();
        s16.Should().Be("credits");

        ActionSymbolRegistry.TryGetSymbol("unfreeze_symbol", out var s17).Should().BeTrue();
        s17.Should().Be("credits");
    }

    #endregion

    #region FileAuditLogger

    [Fact]
    public async Task FileAuditLogger_ShouldWriteRecord_WithDefaultDirectory()
    {
        var logger = new FileAuditLogger();
        var context = new ActionContext("base_swfoc", 42, "set_credits", AddressSource.Signature);
        var record = new ActionAuditRecord(DateTimeOffset.UtcNow, context, true, "Test audit record");

        await logger.WriteAsync(record, CancellationToken.None);
    }

    [Fact]
    public async Task FileAuditLogger_ParameterlessWriteOverload_ShouldDelegate()
    {
        var logger = new FileAuditLogger();
        var context = new ActionContext("base_swfoc", 42, "set_credits", AddressSource.Signature);
        var record = new ActionAuditRecord(DateTimeOffset.UtcNow, context, true, "Test overload");

        await logger.WriteAsync(record);
    }

    [Fact]
    public async Task FileAuditLogger_ShouldThrow_WhenRecordIsNull()
    {
        var logger = new FileAuditLogger();

        var act1 = async () => await logger.WriteAsync(null!, CancellationToken.None);
        var act2 = async () => await logger.WriteAsync(null!);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FileAuditLogger_ShouldWriteToCustomDirectory()
    {
        // The custom directory must be under the trusted root (AppData\Local\SwfocTrainer)
        var appRoot = TrustedPathPolicy.GetOrCreateAppDataRoot();
        var customDir = Path.Join(appRoot, $"audit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(customDir);
        try
        {
            var logger = new FileAuditLogger(customDir);
            var context = new ActionContext("test_profile", 99, "test_action", AddressSource.Fallback);
            var record = new ActionAuditRecord(DateTimeOffset.UtcNow, context, true, "Custom dir test");

            await logger.WriteAsync(record, CancellationToken.None);

            var logFiles = Directory.GetFiles(customDir, "audit-*.jsonl");
            logFiles.Should().NotBeEmpty();
        }
        finally
        {
            try { Directory.Delete(customDir, true); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }

    #endregion

    #region NullSdkDiagnosticsSink

    [Fact]
    public async Task NullSdkDiagnosticsSink_ShouldNotThrow()
    {
        var sink = new NullSdkDiagnosticsSink();
        sink.Should().NotBeNull();

        var request = new SdkOperationRequest("test_op", new System.Text.Json.Nodes.JsonObject(), false, RuntimeMode.Galactic, "test_profile");
        var result = new SdkOperationResult(true, "ok", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available, null);

        await sink.WriteAsync(request, result);
        await sink.WriteAsync(request, result, CancellationToken.None);
    }

    [Fact]
    public async Task NullSdkDiagnosticsSink_ShouldThrow_WhenArgumentsAreNull()
    {
        var sink = new NullSdkDiagnosticsSink();
        var request = new SdkOperationRequest("op", new System.Text.Json.Nodes.JsonObject(), false, RuntimeMode.Galactic, "profile");
        var result = new SdkOperationResult(true, "ok", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available, null);

        var act1 = async () => await sink.WriteAsync(null!, result);
        var act2 = async () => await sink.WriteAsync(request, null!);
        var act3 = async () => await sink.WriteAsync(null!, result, CancellationToken.None);
        var act4 = async () => await sink.WriteAsync(request, null!, CancellationToken.None);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
        await act3.Should().ThrowAsync<ArgumentNullException>();
        await act4.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static TrainerProfile BuildProfile(Dictionary<string, string>? metadata)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    #endregion
}
