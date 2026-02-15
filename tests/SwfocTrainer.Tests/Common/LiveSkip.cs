using Xunit.Abstractions;
using Xunit;

namespace SwfocTrainer.Tests.Common;

internal static class LiveSkip
{
    public static Exception For(ITestOutputHelper output, string reason)
    {
        output.WriteLine($"SKIP — {reason}");
        return new SkipException(reason);
    }
}
