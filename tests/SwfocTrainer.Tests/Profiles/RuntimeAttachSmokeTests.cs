using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class RuntimeAttachSmokeTests
{
    [Fact]
    public async Task RuntimeAdapter_Should_Attach_And_Detach_When_Swfoc_Process_Is_Running()
    {
        var locator = new ProcessLocator();
        var running = await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        if (running is null)
        {
            return;
        }

        var root = TestPaths.FindRepoRoot();
        var options = new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(root, "profiles", "default")
        };

        var repository = new FileSystemProfileRepository(options);
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var adapter = new RuntimeAdapter(locator, repository, resolver, NullLogger<RuntimeAdapter>.Instance);

        var session = await adapter.AttachAsync("base_swfoc");
        session.Process.ExeTarget.Should().Be(ExeTarget.Swfoc);
        session.Process.ProcessId.Should().BeGreaterThan(0);
        session.Symbols.Symbols.Count.Should().BeGreaterThan(0);
        adapter.IsAttached.Should().BeTrue();

        await adapter.DetachAsync();
        adapter.IsAttached.Should().BeFalse();
    }
}
