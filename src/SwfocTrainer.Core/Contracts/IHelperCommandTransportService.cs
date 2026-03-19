using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IHelperCommandTransportService
{
    Task<HelperCommandTransportLayout> GetLayoutAsync(string profileId, CancellationToken cancellationToken);

    Task<HelperStagedCommand> StageCommandAsync(
        string profileId,
        string actionId,
        string helperEntryPoint,
        string operationToken,
        JsonObject payload,
        CancellationToken cancellationToken);

    Task<HelperCommandClaim?> TryReadClaimAsync(
        string profileId,
        string operationToken,
        CancellationToken cancellationToken);

    Task<HelperCommandReceipt?> TryReadReceiptAsync(
        string profileId,
        string operationToken,
        CancellationToken cancellationToken);
}
