using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProcessLocator
{
    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync()
    {
        return FindSupportedProcessesAsync(CancellationToken.None);
    }

    Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken);

    Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target)
    {
        return FindBestMatchAsync(target, CancellationToken.None);
    }
}
