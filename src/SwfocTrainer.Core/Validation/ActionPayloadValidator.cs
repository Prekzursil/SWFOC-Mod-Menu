using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Validation;

public static class ActionPayloadValidator
{
    public static (bool IsValid, string Message) Validate(JsonObject schema, JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(payload);
        if (schema.Count == 0)
        {
            return (true, "No payload schema defined");
        }

        if (schema["required"] is JsonArray requiredArray)
        {
            var missingField = requiredArray
                .Select(r => r?.GetValue<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .FirstOrDefault(name => !payload.ContainsKey(name!));

            if (missingField is not null)
            {
                return (false, $"Missing required payload field: {missingField}");
            }
        }

        return (true, "Payload validated");
    }
}
