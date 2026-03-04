using FluentAssertions;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class JsonProfileSerializerTests
{
    private sealed record TestPayload(string Name, RuntimeMode Mode);

    [Fact]
    public void SerializeAndDeserialize_ShouldRoundTripPayload()
    {
        var payload = new TestPayload("alpha", RuntimeMode.Galactic);

        var json = JsonProfileSerializer.Serialize(payload);
        var restored = JsonProfileSerializer.Deserialize<TestPayload>(json);

        json.Should().Contain("\"Mode\": \"Galactic\"");
        restored.Should().NotBeNull();
        restored!.Name.Should().Be("alpha");
        restored.Mode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void Deserialize_ShouldUseCaseInsensitivePropertyNames()
    {
        const string json = "{\"NAME\":\"bravo\",\"mode\":\"TacticalLand\"}";

        var restored = JsonProfileSerializer.Deserialize<TestPayload>(json);

        restored.Should().NotBeNull();
        restored!.Name.Should().Be("bravo");
        restored.Mode.Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public void ToJsonObject_ShouldReturnEmptyObject_ForNonObjectNodes()
    {
        var result = JsonProfileSerializer.ToJsonObject(123);

        result.Should().NotBeNull();
        result.AsObject().Should().BeEmpty();
    }

    [Fact]
    public void ToJsonObject_ShouldReturnObject_ForComplexValues()
    {
        var result = JsonProfileSerializer.ToJsonObject(new TestPayload("charlie", RuntimeMode.Menu));

        result.Should().ContainKey("Name");
        result["Name"]!.GetValue<string>().Should().Be("charlie");
        result["Mode"]!.GetValue<string>().Should().Be("Menu");
    }
}
