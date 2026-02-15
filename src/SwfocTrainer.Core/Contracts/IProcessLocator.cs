using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProcessLocator
{
    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken = default);

    Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken = default);
}
