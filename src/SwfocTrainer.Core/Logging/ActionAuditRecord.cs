using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Logging;

public sealed record ActionContext
{
    public string ProfileId { get; init; }
    public int ProcessId { get; init; }
    public string ActionId { get; init; }
    public AddressSource AddressSource { get; init; }

    public ActionContext(
        string profileId,
        int processId,
        string actionId,
        AddressSource addressSource)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(actionId);
        ProfileId = profileId;
        ProcessId = processId;
        ActionId = actionId;
        AddressSource = addressSource;
    }
}

public sealed record ActionAuditRecord
{
    public DateTimeOffset Timestamp { get; init; }
    public ActionContext Context { get; init; }
    public bool Succeeded { get; init; }
    public string Message { get; init; }
    public IReadOnlyDictionary<string, object?>? Diagnostics { get; init; }

    public ActionAuditRecord(
        DateTimeOffset timestamp,
        ActionContext context,
        bool succeeded,
        string message)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);
        Timestamp = timestamp;
        Context = context;
        Succeeded = succeeded;
        Message = message;
    }
}
