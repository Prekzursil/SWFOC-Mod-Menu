namespace SwfocTrainer.Core.Models;

public sealed record HelperCommandTransportLayout(
    string ProfileId,
    string DeploymentRoot,
    string ManifestPath,
    string BootstrapScriptPath,
    string Model,
    string SchemaVersion,
    string DispatchCommandPath,
    string PendingDirectory,
    string ClaimedDirectory,
    string ReceiptDirectory);

public sealed record HelperStagedCommand(
    string ProfileId,
    string ActionId,
    string HelperEntryPoint,
    string OperationToken,
    string CommandPath,
    string ClaimPath,
    string ReceiptPath,
    string PayloadPath);

public sealed record HelperCommandClaim(
    string ProfileId,
    string ActionId,
    string HelperEntryPoint,
    string OperationToken,
    string ClaimPath,
    string StageState,
    string Message);

public sealed record HelperCommandReceipt(
    string ProfileId,
    string ActionId,
    string HelperEntryPoint,
    string OperationToken,
    string ReceiptPath,
    string StageState,
    bool Applied,
    string ReasonCode,
    string Message,
    string VerificationSource,
    string VerifyState,
    string AppliedEntityId);
