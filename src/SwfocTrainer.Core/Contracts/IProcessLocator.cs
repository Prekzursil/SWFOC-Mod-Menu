using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProcessLocator
{
    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(
        ProcessLocatorOptions options,
        CancellationToken cancellationToken)
    {
        return FindSupportedProcessesAsync(cancellationToken);
    }

    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync()
    {
        return FindSupportedProcessesAsync(CancellationToken.None);
    }

    Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(ProcessLocatorOptions options)
    {
        return FindSupportedProcessesAsync(options, CancellationToken.None);
    }

    Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken);

    Task<ProcessMetadata?> FindBestMatchAsync(
        ExeTarget target,
        ProcessLocatorOptions options,
        CancellationToken cancellationToken)
    {
        return FindBestMatchAsync(target, cancellationToken);
    }

    Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target)
    {
        return FindBestMatchAsync(target, CancellationToken.None);
    }

    Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, ProcessLocatorOptions options)
    {
        return FindBestMatchAsync(target, options, CancellationToken.None);
    }
}
