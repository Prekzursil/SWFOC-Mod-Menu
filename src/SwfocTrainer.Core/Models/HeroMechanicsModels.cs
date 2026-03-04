namespace SwfocTrainer.Core.Models;

[System.CLSCompliant(false)]
public sealed record HeroMechanicsProfile(
    bool SupportsRespawn,
    bool SupportsPermadeath,
    bool SupportsRescue,
    int? DefaultRespawnTime,
    IReadOnlyList<string> RespawnExceptionSources,
    string DuplicateHeroPolicy,
    IReadOnlyDictionary<string, string>? Diagnostics = null)
{
    public static HeroMechanicsProfile Empty()
        => new(
            SupportsRespawn: false,
            SupportsPermadeath: false,
            SupportsRescue: false,
            DefaultRespawnTime: null,
            RespawnExceptionSources: Array.Empty<string>(),
            DuplicateHeroPolicy: "unknown",
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

[System.CLSCompliant(false)]
public sealed record HeroEditRequest(
    string TargetHeroId,
    string DesiredState,
    string? RespawnPolicyOverride = null,
    bool AllowDuplicate = false,
    string? TargetFaction = null,
    string? SourceFaction = null,
    IReadOnlyDictionary<string, object?>? Parameters = null);

[System.CLSCompliant(false)]
public sealed record HeroVariantRequest(
    string SourceHeroId,
    string VariantHeroId,
    string DisplayName,
    IReadOnlyDictionary<string, object?>? StatOverrides = null,
    IReadOnlyDictionary<string, object?>? AbilityOverrides = null,
    bool ReplaceExisting = false);
