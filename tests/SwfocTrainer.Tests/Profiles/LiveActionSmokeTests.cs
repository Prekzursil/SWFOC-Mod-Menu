using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Profiles;

// #lizard forgive global
public sealed class LiveActionSmokeTests
{
    private readonly ITestOutputHelper _output;

    private sealed record LiveSmokeReadState(
        int? Credits,
        int? HeroRespawn,
        float? InstantBuild,
        float? SelectedHp,
        int? PlanetOwner,
        byte? Fog,
        byte? TimerFreeze,
        byte? TacticalGod,
        byte? TacticalOneHit,
        IReadOnlyList<string> ReadFailures);

    public LiveActionSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task LiveSmoke_Attach_Read_And_OptionalToggleRevert_Should_Succeed()
    {
        var locator = new ProcessLocator();
        var running = await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        if (running is null)
        {
            return;
        }

        var repoRoot = TestPaths.FindRepoRoot();
        var profileRepo = new FileSystemProfileRepository(new ProfileRepositoryOptions
        {
            ProfilesRootPath = Path.Combine(repoRoot, "profiles", "default")
        });

        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance);
        var runtime = new RuntimeAdapter(locator, profileRepo, resolver, NullLogger<RuntimeAdapter>.Instance);

        var profiles = await ResolveProfilesAsync(profileRepo);
        var context = running.LaunchContext ?? new LaunchContextResolver().Resolve(running, profiles);
        var profileId = context.Recommendation.ProfileId ?? "base_swfoc";

        _output.WriteLine(
            $"Selected profile for live smoke: {profileId} (reason={context.Recommendation.ReasonCode}, confidence={context.Recommendation.Confidence:0.00})");
        var session = await runtime.AttachAsync(profileId);
        session.Process.ProcessId.Should().Be(running.ProcessId);
        session.Symbols.Symbols.Count.Should().BeGreaterThan(0);

        LogResolvedSymbols(session);
        var readState = await ReadSmokeSymbolsAsync(runtime);

        DumpConditionalDebugScans(session, readState);
        LogReadFailures(readState.ReadFailures);
        await RunOptionalToggleRevertChecksAsync(runtime, readState);
        await AssertOptionalToggleValuesAsync(runtime, readState);

        await runtime.DetachAsync();
        runtime.IsAttached.Should().BeFalse();
    }

    private void LogResolvedSymbols(AttachSession session)
    {
        _output.WriteLine($"Resolved symbols: {session.Symbols.Symbols.Count}");
        foreach (var symbolName in session.Symbols.Symbols.Keys.OrderBy(x => x))
        {
            if (session.Symbols.Symbols.TryGetValue(symbolName, out var symbol))
            {
                _output.WriteLine(
                    $"{symbolName}: 0x{symbol.Address.ToInt64():X} source={symbol.Source} diag={symbol.Diagnostics}");
            }
        }
    }

    private async Task<LiveSmokeReadState> ReadSmokeSymbolsAsync(RuntimeAdapter runtime)
    {
        var readFailures = new List<string>();
        var credits = await TryReadSymbolAsync<int>(runtime, "credits", "Credits", readFailures);
        var heroRespawn = await TryReadSymbolAsync<int>(runtime, "hero_respawn_timer", "hero_respawn_timer", readFailures);
        var instantBuild = await TryReadSymbolAsync<float>(runtime, "instant_build", "instant_build", readFailures);
        var selectedHp = await TryReadSymbolAsync<float>(runtime, "selected_hp", "selected_hp", readFailures);
        var planetOwner = await TryReadSymbolAsync<int>(runtime, "planet_owner", "planet_owner", readFailures);
        var fog = await TryReadSymbolAsync<byte>(runtime, "fog_reveal", "fog_reveal", readFailures);
        var timerFreeze = await TryReadSymbolAsync<byte>(runtime, "game_timer_freeze", "game_timer_freeze", readFailures);
        var tacticalGod = await TryReadSymbolAsync<byte>(runtime, "tactical_god_mode", "tactical_god_mode", readFailures);
        var tacticalOneHit = await TryReadSymbolAsync<byte>(runtime, "tactical_one_hit_mode", "tactical_one_hit_mode", readFailures);

        return new LiveSmokeReadState(
            Credits: credits,
            HeroRespawn: heroRespawn,
            InstantBuild: instantBuild,
            SelectedHp: selectedHp,
            PlanetOwner: planetOwner,
            Fog: fog,
            TimerFreeze: timerFreeze,
            TacticalGod: tacticalGod,
            TacticalOneHit: tacticalOneHit,
            ReadFailures: readFailures);
    }

    private async Task<T?> TryReadSymbolAsync<T>(
        RuntimeAdapter runtime,
        string symbolName,
        string successLabel,
        List<string> readFailures)
        where T : unmanaged
    {
        try
        {
            var value = await runtime.ReadAsync<T>(symbolName);
            _output.WriteLine($"{successLabel} read succeeded: {value}");
            return value;
        }
        catch (Exception ex)
        {
            readFailures.Add($"{symbolName}: {ex.Message}");
            return null;
        }
    }

    private void DumpConditionalDebugScans(AttachSession session, LiveSmokeReadState readState)
    {
        if (readState.ReadFailures.Count == 0)
        {
            return;
        }

        _output.WriteLine("Debug scan hint: live read failures detected.");
        _output.WriteLine($" - processId={session.Process.ProcessId}");
        _output.WriteLine($" - creditsReadable={readState.Credits.HasValue}");
        _output.WriteLine($" - fogReadable={readState.Fog.HasValue}");
        _output.WriteLine($" - timerReadable={readState.TimerFreeze.HasValue}");
        _output.WriteLine($" - selectedHpReadable={readState.SelectedHp.HasValue}");
        _output.WriteLine($" - planetOwnerReadable={readState.PlanetOwner.HasValue}");
    }

    private void LogReadFailures(IReadOnlyList<string> readFailures)
    {
        if (readFailures.Count <= 0)
        {
            return;
        }

        _output.WriteLine("Read failures:");
        foreach (var failure in readFailures)
        {
            _output.WriteLine($" - {failure}");
        }
    }

    private async Task RunOptionalToggleRevertChecksAsync(RuntimeAdapter runtime, LiveSmokeReadState readState)
    {
        if (!HasReadableToggleTargets(readState))
        {
            _output.WriteLine("No target symbols were readable in this build. Likely requires signature/offset calibration.");
            return;
        }

        await RestoreKnownToggleValuesAsync(runtime, readState);
        var toggledAny = false;
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "fog_reveal", readState.Fog);
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "game_timer_freeze", readState.TimerFreeze);
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "tactical_god_mode", readState.TacticalGod);
        toggledAny |= await TryToggleByteSymbolAsync(runtime, "tactical_one_hit_mode", readState.TacticalOneHit);

        if (!toggledAny)
        {
            _output.WriteLine("No toggle-safe symbols were readable in this build.");
        }
    }

    private static bool HasReadableToggleTargets(LiveSmokeReadState readState) =>
        readState.Credits is not null || readState.Fog is not null || readState.TimerFreeze is not null;

    private static async Task RestoreKnownToggleValuesAsync(RuntimeAdapter runtime, LiveSmokeReadState readState)
    {
        if (readState.Fog.HasValue)
        {
            await runtime.WriteAsync("fog_reveal", readState.Fog.Value);
        }

        if (readState.TimerFreeze.HasValue)
        {
            await runtime.WriteAsync("game_timer_freeze", readState.TimerFreeze.Value);
        }
    }

    private async Task<bool> TryToggleByteSymbolAsync(RuntimeAdapter runtime, string symbolName, byte? originalValue)
    {
        if (!originalValue.HasValue)
        {
            return false;
        }

        var toggledValue = (byte)(originalValue.Value == 0 ? 1 : 0);
        await runtime.WriteAsync(symbolName, toggledValue);
        await runtime.WriteAsync(symbolName, originalValue.Value);
        _output.WriteLine($"Toggle-revert check executed for {symbolName}.");
        return true;
    }

    private static async Task AssertOptionalToggleValuesAsync(RuntimeAdapter runtime, LiveSmokeReadState readState)
    {
        if (readState.Fog.HasValue)
        {
            var fogAfter = await runtime.ReadAsync<byte>("fog_reveal");
            fogAfter.Should().Be(readState.Fog.Value);
        }

        if (readState.TimerFreeze.HasValue)
        {
            var timerAfter = await runtime.ReadAsync<byte>("game_timer_freeze");
            timerAfter.Should().Be(readState.TimerFreeze.Value);
        }
    }

    private static async Task<IReadOnlyList<TrainerProfile>> ResolveProfilesAsync(FileSystemProfileRepository profileRepository)
    {
        var ids = await profileRepository.ListAvailableProfilesAsync();
        var profiles = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            profiles.Add(await profileRepository.ResolveInheritedProfileAsync(id));
        }

        return profiles;
    }
}
