using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelSessionGatingTests
{
    [Fact]
    public void ResolveActionUnavailableReason_SdkSymbolActionWithoutResolvedSymbol_ShouldReturnUnresolvedReason()
    {
        var action = BuildAction("set_credits", ExecutionKind.Sdk, "symbol", "intValue");
        var session = BuildSession(RuntimeMode.Galactic, symbol: null);

        var reason = InvokeResolveActionUnavailableReason("set_credits", action, session);

        reason.Should().Be("required symbol 'credits' is unresolved for this attachment.");
    }

    [Fact]
    public void ResolveActionUnavailableReason_SdkSymbolActionWithUnresolvedSymbolEntry_ShouldReturnUnresolvedReason()
    {
        var action = BuildAction("set_credits", ExecutionKind.Sdk, "symbol", "intValue");
        var session = BuildSession(
            RuntimeMode.Galactic,
            new SymbolInfo(
                Name: "credits",
                Address: nint.Zero,
                ValueType: SymbolValueType.Int32,
                Source: AddressSource.Signature,
                HealthStatus: SymbolHealthStatus.Unresolved));

        var reason = InvokeResolveActionUnavailableReason("set_credits", action, session);

        reason.Should().Be("required symbol 'credits' is unresolved for this attachment.");
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_FallbackActionDisabled_ShouldReturnReason()
    {
        var profile = BuildProfile(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow_fog_patch_fallback"] = false
        });

        var reason = InvokeResolveProfileFeatureGateReason("toggle_fog_reveal_patch_fallback", profile);

        reason.Should().Contain("allow_fog_patch_fallback");
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_FallbackActionEnabled_ShouldReturnNull()
    {
        var profile = BuildProfile(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow_unit_cap_patch_fallback"] = true
        });

        var reason = InvokeResolveProfileFeatureGateReason("set_unit_cap_patch_fallback", profile);

        reason.Should().BeNull();
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_ExtenderCreditsDisabled_ShouldReturnReason()
    {
        var profile = BuildProfile(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow_extender_credits"] = false
        });

        var reason = InvokeResolveProfileFeatureGateReason("set_credits_extender_experimental", profile);

        reason.Should().Contain("allow_extender_credits");
    }

    [Fact]
    public void ResolveProfileFeatureGateReason_ExtenderCreditsEnabled_ShouldReturnNull()
    {
        var profile = BuildProfile(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["allow_extender_credits"] = true
        });

        var reason = InvokeResolveProfileFeatureGateReason("set_credits_extender_experimental", profile);

        reason.Should().BeNull();
    }

    private static string? InvokeResolveActionUnavailableReason(
        string actionId,
        ActionSpec action,
        AttachSession session)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveActionUnavailableReason",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("MainViewModel should gate unresolved symbol actions before runtime dispatch.");
        var result = method!.Invoke(null, new object?[] { actionId, action, session });
        return result as string;
    }

    private static string? InvokeResolveProfileFeatureGateReason(string actionId, TrainerProfile profile)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveProfileFeatureGateReason",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("MainViewModel should gate profile-disabled fallback actions before execution.");
        var result = method!.Invoke(null, new object?[] { actionId, profile });
        return result as string;
    }

    private static ActionSpec BuildAction(string actionId, ExecutionKind executionKind, params string[] requiredPayloadFields)
    {
        var required = new JsonArray(requiredPayloadFields.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        return new ActionSpec(
            actionId,
            ActionCategory.Global,
            RuntimeMode.Unknown,
            executionKind,
            new JsonObject { ["required"] = required },
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private static AttachSession BuildSession(RuntimeMode runtimeMode, SymbolInfo? symbol)
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        if (symbol is not null)
        {
            symbols[symbol.Name] = symbol;
        }

        return new AttachSession(
            ProfileId: "test_profile",
            Process: new ProcessMetadata(
                ProcessId: 123,
                ProcessName: "swfoc",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: null,
                ExeTarget: ExeTarget.Swfoc,
                Mode: runtimeMode,
                Metadata: null),
            Build: new ProfileBuild("test_profile", "test_build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbols),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static TrainerProfile BuildProfile(IReadOnlyDictionary<string, bool> featureFlags)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: featureFlags,
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
