using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class OwnershipTransferService : IOwnershipTransferService
{
    internal const string FeatureId = "v5_ownership_transfer";

    private readonly ILuaBridgeExecutor? _bridge;
    private readonly ILogger<OwnershipTransferService> _logger;

    public OwnershipTransferService(
        ILuaBridgeExecutor bridge,
        ILogger<OwnershipTransferService> logger)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = bridge;
        _logger = logger;
    }

    public OwnershipTransferService(ILogger<OwnershipTransferService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _bridge = null;
        _logger = logger;
    }

    public async Task<ActionExecutionResult> TransferOwnershipAsync(
        string profileId, OwnershipTransferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(request);

        var scopeLabel = ResolveScopeLabel(request.Scope);
        var luaCommand = BuildOwnershipLuaCommand(request.TargetId, request.NewOwnerFaction);

        _logger.LogInformation(
            "Ownership transfer executing: {TargetId} -> {NewOwner} (scope: {Scope}) for profile {ProfileId}",
            request.TargetId, request.NewOwnerFaction, scopeLabel, profileId);

        if (_bridge is not null)
        {
            return await _bridge.ExecuteLuaAsync(profileId, luaCommand, FeatureId, cancellationToken);
        }

        return new ActionExecutionResult(
            Succeeded: true,
            Message: $"Ownership transfer prepared ({scopeLabel}): {request.TargetId} -> {request.NewOwnerFaction}",
            AddressSource: AddressSource.None);
    }

    /// <summary>
    /// Builds the Lua command string for an ownership transfer.
    /// Uses colon syntax for method call on the found game object.
    /// </summary>
    internal static string BuildOwnershipLuaCommand(string targetId, string newOwner)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(newOwner);
        return $"Find_First_Object(\"{targetId}\"):Change_Owner(Find_Player(\"{newOwner}\"))";
    }

    internal static string ResolveScopeLabel(OwnershipTransferScope scope)
    {
        return scope switch
        {
            OwnershipTransferScope.SelectedUnit => "selected_unit",
            OwnershipTransferScope.AllOfType => "all_of_type",
            OwnershipTransferScope.AllVisible => "all_visible",
            OwnershipTransferScope.Planet => "planet",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown ownership transfer scope")
        };
    }
}
