using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// xUnit collection definition that lets every replay test class share a
/// SINGLE <see cref="ReplayHarnessFixture"/> instance — and therefore a
/// single <c>swfoc_replay.exe</c> process and a single
/// <c>\\.\pipe\swfoc_bridge_replay</c> binding.
/// </summary>
/// <remarks>
/// The replay binary creates the pipe with <c>maxNumberOfServerInstances=1</c>
/// (see <c>replay_harness.cpp:889</c>). If two test classes each spawn their
/// own fixture via <see cref="IClassFixture{TFixture}"/>, the second instance
/// races to bind the same pipe and the kernel returns
/// <c>ERROR_PIPE_BUSY (231)</c>. Using a collection fixture across all
/// replay test classes guarantees exactly one harness instance for the
/// entire test run.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ReplayHarnessCollection : ICollectionFixture<ReplayHarnessFixture>
{
    public const string Name = "ReplayHarness";
}
