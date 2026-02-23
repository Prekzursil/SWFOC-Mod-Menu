namespace SwfocTrainer.Core.Models;

/// <summary>
/// Canonical SDK operation catalog for the research runtime path.
/// </summary>
public static class SdkOperationCatalog
{
    private static readonly IReadOnlyDictionary<string, SdkOperationDefinition> Operations =
        new Dictionary<string, SdkOperationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["list_selected"] = SdkOperationDefinition.ReadOnly("list_selected"),
            ["list_nearby"] = SdkOperationDefinition.ReadOnly("list_nearby"),
            ["spawn"] = SdkOperationDefinition.Mutation("spawn", RuntimeMode.Tactical, RuntimeMode.Galactic),
            ["kill"] = SdkOperationDefinition.Mutation("kill", RuntimeMode.Tactical),
            ["set_owner"] = SdkOperationDefinition.Mutation("set_owner", RuntimeMode.Tactical, RuntimeMode.Galactic),
            ["teleport"] = SdkOperationDefinition.Mutation("teleport", RuntimeMode.Tactical),
            ["set_planet_owner"] = SdkOperationDefinition.Mutation("set_planet_owner", RuntimeMode.Galactic),
            ["set_hp"] = SdkOperationDefinition.Mutation("set_hp", RuntimeMode.Tactical),
            ["set_shield"] = SdkOperationDefinition.Mutation("set_shield", RuntimeMode.Tactical),
            ["set_cooldown"] = SdkOperationDefinition.Mutation("set_cooldown", RuntimeMode.Tactical)
        };

    public static bool TryGet(string operationId, out SdkOperationDefinition definition)
    {
        return Operations.TryGetValue(operationId, out definition!);
    }

    public static IReadOnlyCollection<SdkOperationDefinition> List() => Operations.Values.ToArray();
}

/// <summary>
/// SDK operation metadata used by router gating.
/// </summary>
public sealed record SdkOperationDefinition(
    string OperationId,
    bool IsMutation,
    IReadOnlySet<RuntimeMode> AllowedModes,
    bool AllowUnknownMode)
{
    public bool IsModeAllowed(RuntimeMode mode)
    {
        if (mode == RuntimeMode.Unknown)
        {
            return AllowUnknownMode;
        }

        if (AllowedModes.Count == 0)
        {
            return true;
        }

        return AllowedModes.Contains(mode);
    }

    public static SdkOperationDefinition ReadOnly(string operationId)
    {
        return new SdkOperationDefinition(
            operationId,
            IsMutation: false,
            AllowedModes: new HashSet<RuntimeMode>(),
            AllowUnknownMode: true);
    }

    public static SdkOperationDefinition Mutation(string operationId, params RuntimeMode[] allowedModes)
    {
        return new SdkOperationDefinition(
            operationId,
            IsMutation: true,
            AllowedModes: new HashSet<RuntimeMode>(allowedModes),
            AllowUnknownMode: false);
    }
}
