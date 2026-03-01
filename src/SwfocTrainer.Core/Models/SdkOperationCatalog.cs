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
            ["spawn"] = SdkOperationDefinition.Mutation("spawn", RuntimeMode.AnyTactical, RuntimeMode.Galactic),
            ["kill"] = SdkOperationDefinition.Mutation("kill", RuntimeMode.AnyTactical),
            ["set_owner"] = SdkOperationDefinition.Mutation("set_owner", RuntimeMode.AnyTactical, RuntimeMode.Galactic),
            ["teleport"] = SdkOperationDefinition.Mutation("teleport", RuntimeMode.AnyTactical),
            ["set_planet_owner"] = SdkOperationDefinition.Mutation("set_planet_owner", RuntimeMode.Galactic),
            ["set_hp"] = SdkOperationDefinition.Mutation("set_hp", RuntimeMode.AnyTactical),
            ["set_shield"] = SdkOperationDefinition.Mutation("set_shield", RuntimeMode.AnyTactical),
            ["set_cooldown"] = SdkOperationDefinition.Mutation("set_cooldown", RuntimeMode.AnyTactical)
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

        if (AllowedModes.Contains(mode))
        {
            return true;
        }

        if (mode is RuntimeMode.TacticalLand or RuntimeMode.TacticalSpace)
        {
            return AllowedModes.Contains(RuntimeMode.AnyTactical);
        }

        return false;
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
