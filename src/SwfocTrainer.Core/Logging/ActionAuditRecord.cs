using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Logging;

public sealed record ActionAuditRecord(
    DateTimeOffset Timestamp,
    string ProfileId,
    int ProcessId,
    string ActionId,
    AddressSource AddressSource,
    bool Succeeded,
    string Message,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
