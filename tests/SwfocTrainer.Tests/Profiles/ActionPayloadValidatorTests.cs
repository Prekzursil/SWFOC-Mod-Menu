using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Validation;
using Xunit;

namespace SwfocTrainer.Tests.Profiles;

public sealed class ActionPayloadValidatorTests
{
    [Fact]
    public void Validator_Should_Fail_When_Required_Field_Missing()
    {
        var schema = new JsonObject
        {
            ["required"] = new JsonArray("symbol", "intValue")
        };

        var payload = new JsonObject
        {
            ["symbol"] = "credits"
        };

        var result = ActionPayloadValidator.Validate(schema, payload);
        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("intValue");
    }

    [Fact]
    public void Validator_Should_Pass_When_All_Required_Present()
    {
        var schema = new JsonObject
        {
            ["required"] = new JsonArray("symbol", "intValue")
        };

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 1000
        };

        var result = ActionPayloadValidator.Validate(schema, payload);
        result.IsValid.Should().BeTrue();
    }
}
