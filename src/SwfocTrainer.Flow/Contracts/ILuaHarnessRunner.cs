using SwfocTrainer.Flow.Models;

namespace SwfocTrainer.Flow.Contracts;

public interface ILuaHarnessRunner
{
    Task<LuaHarnessRunResult> RunAsync(LuaHarnessRunRequest request, CancellationToken cancellationToken = default);
}
