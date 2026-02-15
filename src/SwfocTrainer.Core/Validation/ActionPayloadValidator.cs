using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Validation;

public static class ActionPayloadValidator
{
    public static (bool IsValid, string Message) Validate(JsonObject schema, JsonObject payload)
    {
        if (schema.Count == 0)
        {
            return (true, "No payload schema defined");
        }

        if (schema["required"] is JsonArray requiredArray)
        {
            foreach (var required in requiredArray)
            {
                var name = required?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!payload.ContainsKey(name))
                {
                    return (false, $"Missing required payload field: {name}");
                }
            }
        }

        return (true, "Payload validated");
    }
}
