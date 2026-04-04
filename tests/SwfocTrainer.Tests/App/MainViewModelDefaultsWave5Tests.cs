using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelDefaults:
/// Validates all constant values and dictionary lookups to ensure full coverage
/// of the static initializer and all dictionary entries.
/// </summary>
public sealed class MainViewModelDefaultsWave5Tests
{
    [Fact]
    public void ActionConstants_ShouldHaveExpectedValues()
    {
        MainViewModelDefaults.ActionSetCredits.Should().Be("set_credits");
        MainViewModelDefaults.ActionFreezeTimer.Should().Be("freeze_timer");
        MainViewModelDefaults.ActionToggleFogReveal.Should().Be("toggle_fog_reveal");
        MainViewModelDefaults.ActionToggleAi.Should().Be("toggle_ai");
        MainViewModelDefaults.ActionSetUnitCap.Should().Be("set_unit_cap");
        MainViewModelDefaults.ActionToggleInstantBuildPatch.Should().Be("toggle_instant_build_patch");
        MainViewModelDefaults.ActionToggleTacticalGodMode.Should().Be("toggle_tactical_god_mode");
        MainViewModelDefaults.ActionToggleTacticalOneHitMode.Should().Be("toggle_tactical_one_hit_mode");
        MainViewModelDefaults.ActionSetGameSpeed.Should().Be("set_game_speed");
        MainViewModelDefaults.ActionFreezeSymbol.Should().Be("freeze_symbol");
        MainViewModelDefaults.ActionUnfreezeSymbol.Should().Be("unfreeze_symbol");
    }

    [Fact]
    public void SymbolConstants_ShouldHaveExpectedValues()
    {
        MainViewModelDefaults.SymbolCredits.Should().Be("credits");
        MainViewModelDefaults.SymbolGameTimerFreeze.Should().Be("game_timer_freeze");
        MainViewModelDefaults.SymbolFogReveal.Should().Be("fog_reveal");
        MainViewModelDefaults.SymbolAiEnabled.Should().Be("ai_enabled");
        MainViewModelDefaults.SymbolUnitCap.Should().Be("unit_cap");
        MainViewModelDefaults.SymbolInstantBuildNop.Should().Be("instant_build_nop");
        MainViewModelDefaults.SymbolTacticalGodMode.Should().Be("tactical_god_mode");
        MainViewModelDefaults.SymbolTacticalOneHitMode.Should().Be("tactical_one_hit_mode");
        MainViewModelDefaults.SymbolGameSpeed.Should().Be("game_speed");
    }

    [Fact]
    public void PayloadConstants_ShouldHaveExpectedValues()
    {
        MainViewModelDefaults.PayloadSymbol.Should().Be("symbol");
        MainViewModelDefaults.PayloadIntValue.Should().Be("intValue");
        MainViewModelDefaults.PayloadBoolValue.Should().Be("boolValue");
        MainViewModelDefaults.PayloadEnable.Should().Be("enable");
        MainViewModelDefaults.PayloadFloatValue.Should().Be("floatValue");
        MainViewModelDefaults.PayloadFreeze.Should().Be("freeze");
        MainViewModelDefaults.PayloadLockCredits.Should().Be("lockCredits");
    }

    [Fact]
    public void NumericDefaults_ShouldHaveExpectedValues()
    {
        MainViewModelDefaults.DefaultCreditsValue.Should().Be(1000000);
        MainViewModelDefaults.DefaultUnitCapValue.Should().Be(99999);
        MainViewModelDefaults.DefaultGameSpeedValue.Should().Be(2.0f);
    }

    [Fact]
    public void StringDefaults_ShouldHaveExpectedValues()
    {
        MainViewModelDefaults.BaseSwfocProfileId.Should().Be("base_swfoc");
        MainViewModelDefaults.DefaultLaunchTarget.Should().Be("Swfoc");
        MainViewModelDefaults.DefaultLaunchMode.Should().Be("Vanilla");
        MainViewModelDefaults.DefaultCreditsValueText.Should().Be("1000000");
        MainViewModelDefaults.DefaultPayloadJsonTemplate.Should().Contain("credits");
    }

    [Fact]
    public void DefaultSymbolByActionId_ShouldContainAllExpectedMappings()
    {
        var map = MainViewModelDefaults.DefaultSymbolByActionId;
        map.Should().ContainKey("read_symbol").WhoseValue.Should().Be("credits");
        map.Should().ContainKey("set_credits").WhoseValue.Should().Be("credits");
        map.Should().ContainKey("freeze_timer").WhoseValue.Should().Be("game_timer_freeze");
        map.Should().ContainKey("toggle_fog_reveal").WhoseValue.Should().Be("fog_reveal");
        map.Should().ContainKey("toggle_ai").WhoseValue.Should().Be("ai_enabled");
        map.Should().ContainKey("set_instant_build_multiplier").WhoseValue.Should().Be("instant_build");
        map.Should().ContainKey("set_selected_hp").WhoseValue.Should().Be("selected_hp");
        map.Should().ContainKey("set_selected_shield").WhoseValue.Should().Be("selected_shield");
        map.Should().ContainKey("set_selected_speed").WhoseValue.Should().Be("selected_speed");
        map.Should().ContainKey("set_selected_damage_multiplier").WhoseValue.Should().Be("selected_damage_multiplier");
        map.Should().ContainKey("set_selected_cooldown_multiplier").WhoseValue.Should().Be("selected_cooldown_multiplier");
        map.Should().ContainKey("set_selected_veterancy").WhoseValue.Should().Be("selected_veterancy");
        map.Should().ContainKey("set_selected_owner_faction").WhoseValue.Should().Be("selected_owner_faction");
        map.Should().ContainKey("set_planet_owner").WhoseValue.Should().Be("planet_owner");
        map.Should().ContainKey("set_hero_respawn_timer").WhoseValue.Should().Be("hero_respawn_timer");
        map.Should().ContainKey("toggle_tactical_god_mode").WhoseValue.Should().Be("tactical_god_mode");
        map.Should().ContainKey("toggle_tactical_one_hit_mode").WhoseValue.Should().Be("tactical_one_hit_mode");
        map.Should().ContainKey("set_game_speed").WhoseValue.Should().Be("game_speed");
        map.Should().ContainKey("freeze_symbol").WhoseValue.Should().Be("credits");
        map.Should().ContainKey("unfreeze_symbol").WhoseValue.Should().Be("credits");
    }

    [Fact]
    public void DefaultHelperHookByActionId_ShouldContainAllExpectedMappings()
    {
        var map = MainViewModelDefaults.DefaultHelperHookByActionId;
        map.Should().ContainKey("spawn_unit_helper").WhoseValue.Should().Be("spawn_bridge");
        map.Should().ContainKey("set_hero_state_helper").WhoseValue.Should().Be("aotr_hero_state_bridge");
        map.Should().ContainKey("toggle_roe_respawn_helper").WhoseValue.Should().Be("roe_respawn_bridge");
    }
}
