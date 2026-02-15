using SwfocTrainer.Core.Logging;

namespace SwfocTrainer.Core.Contracts;

public interface IAuditLogger
{
    Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken = default);
}
