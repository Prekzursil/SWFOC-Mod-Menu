using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IDebugConsoleFallbackAdapter
{
    SdkFallbackResult Prepare(
        SdkOperationId operationId,
        string profileId,
        RuntimeMode runtimeMode,
        IReadOnlyDictionary<string, object?>? payload = null);
}
