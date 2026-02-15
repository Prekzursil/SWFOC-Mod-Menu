using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Validation;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Expanded payload-validation tests covering CodePatch, Freeze, and Credits action schemas.
/// </summary>
public sealed class PayloadSchemaValidationTests
{
    // ─── Unit cap hook: set_unit_cap ────────────────────────────────────

    private static readonly JsonObject UnitCapSchema = new()
    {
        ["required"] = new JsonArray("intValue")
    };

    [Fact]
    public void UnitCap_Should_Pass_With_All_Required_Fields()
    {
        var payload = new JsonObject
        {
            ["intValue"] = 99999,
            ["enable"] = true
        };

        var result = ActionPayloadValidator.Validate(UnitCapSchema, payload);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UnitCap_Should_Fail_When_IntValue_Missing()
    {
        var payload = new JsonObject
        {
            ["enable"] = true
        };

        var result = ActionPayloadValidator.Validate(UnitCapSchema, payload);
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("intValue");
    }

    [Fact]
    public void UnitCap_Should_Pass_With_Optional_Fields_Omitted()
    {
        var payload = new JsonObject
        {
            ["intValue"] = 99999
        };

        var result = ActionPayloadValidator.Validate(UnitCapSchema, payload);
        result.IsValid.Should().BeTrue();
    }

    // ─── Freeze: freeze_symbol ──────────────────────────────────────────

    private static readonly JsonObject FreezeSchema = new()
    {
        ["required"] = new JsonArray("symbol", "freeze")
    };

    [Fact]
    public void Freeze_Should_Pass_With_Symbol_And_Freeze()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true,
            ["intValue"] = 100000
        };

        var result = ActionPayloadValidator.Validate(FreezeSchema, payload);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Freeze_Should_Fail_When_Freeze_Flag_Missing()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 100000
        };

        var result = ActionPayloadValidator.Validate(FreezeSchema, payload);
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("freeze");
    }

    // ─── Unfreeze: unfreeze_symbol ──────────────────────────────────────

    private static readonly JsonObject UnfreezeSchema = new()
    {
        ["required"] = new JsonArray("symbol")
    };

    [Fact]
    public void Unfreeze_Should_Pass_With_Symbol_Only()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "credits"
        };

        var result = ActionPayloadValidator.Validate(UnfreezeSchema, payload);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Unfreeze_Should_Fail_When_Symbol_Missing()
    {
        var payload = new JsonObject
        {
            ["freeze"] = false
        };

        var result = ActionPayloadValidator.Validate(UnfreezeSchema, payload);
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("symbol");
    }

    // ─── Credits: set_credits ───────────────────────────────────────────

    private static readonly JsonObject CreditsSchema = new()
    {
        ["required"] = new JsonArray("symbol", "intValue")
    };

    [Fact]
    public void Credits_Should_Pass_With_Symbol_And_IntValue()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 1000000
        };

        var result = ActionPayloadValidator.Validate(CreditsSchema, payload);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Credits_Should_Fail_Without_IntValue()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "credits"
        };

        var result = ActionPayloadValidator.Validate(CreditsSchema, payload);
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("intValue");
    }

    // ─── Game speed: set_game_speed ─────────────────────────────────────

    private static readonly JsonObject GameSpeedSchema = new()
    {
        ["required"] = new JsonArray("symbol", "floatValue")
    };

    [Fact]
    public void GameSpeed_Should_Pass_With_FloatValue()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "game_speed",
            ["floatValue"] = 2.0f
        };

        var result = ActionPayloadValidator.Validate(GameSpeedSchema, payload);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void GameSpeed_Should_Fail_Without_FloatValue()
    {
        var payload = new JsonObject
        {
            ["symbol"] = "game_speed"
        };

        var result = ActionPayloadValidator.Validate(GameSpeedSchema, payload);
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("floatValue");
    }

    // ─── Empty schema (no validation) ───────────────────────────────────

    [Fact]
    public void Empty_Schema_Should_Accept_Any_Payload()
    {
        var schema = new JsonObject();
        var payload = new JsonObject
        {
            ["anything"] = "goes",
            ["extra"] = 42
        };

        var result = ActionPayloadValidator.Validate(schema, payload);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Schema_Should_Accept_Empty_Payload()
    {
        var schema = new JsonObject();
        var payload = new JsonObject();

        var result = ActionPayloadValidator.Validate(schema, payload);
        result.IsValid.Should().BeTrue();
    }
}
