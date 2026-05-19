using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// 2026-04-27 (iter 19): xUnit collection definition that serialises every
/// test class touching <c>MainViewModelRuntimeModeOverrideHelpers</c>.
/// Without this, xUnit's default per-class parallel runner lets multiple
/// classes race each other on the shared
/// <c>%APPDATA%\SwfocTrainer\runtime-mode-settings.json</c> file, which
/// produces intermittent <c>IOException: file in use</c> failures
/// (observed across iter 4, iter 13, iter 17 test runs).
/// </summary>
/// <remarks>
/// Apply <c>[Collection(RuntimeModeSerialCollection.Name)]</c> to every
/// test class that calls <c>Save</c> / <c>Load</c> on the helper. xUnit
/// then guarantees those classes run sequentially with respect to each
/// other (still parallel against unrelated collections).
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RuntimeModeSerialCollection
{
    public const string Name = "RuntimeMode (file-based)";
}
