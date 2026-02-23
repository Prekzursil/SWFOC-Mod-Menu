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
}
