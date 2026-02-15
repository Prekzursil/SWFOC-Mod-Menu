using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IRuntimeAdapter
{
    Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken = default);

    Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken = default) where T : unmanaged;

    Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken = default) where T : unmanaged;

    Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken = default);

    Task DetachAsync(CancellationToken cancellationToken = default);

    bool IsAttached { get; }

    AttachSession? CurrentSession { get; }
}
