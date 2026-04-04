using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Logging;

public sealed record ActionAuditRecord
{
    public DateTimeOffset Timestamp { get; init; }
    public string ProfileId { get; init; }
    public int ProcessId { get; init; }
    public string ActionId { get; init; }
    public AddressSource AddressSource { get; init; }
    public bool Succeeded { get; init; }
    public string Message { get; init; }
    public IReadOnlyDictionary<string, object?>? Diagnostics { get; init; }

    public ActionAuditRecord(
        DateTimeOffset timestamp,
        string profileId,
        int processId,
        string actionId,
        AddressSource addressSource,
        bool succeeded,
        string message,
        IReadOnlyDictionary<string, object?>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(actionId);
        ArgumentNullException.ThrowIfNull(message);
        Timestamp = timestamp;
        ProfileId = profileId;
        ProcessId = processId;
        ActionId = actionId;
        AddressSource = addressSource;
        Succeeded = succeeded;
        Message = message;
        Diagnostics = diagnostics;
    }
}
