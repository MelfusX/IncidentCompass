using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class AnalysisWorkerOutputSchemaValidator
{
    public static void Validate(string content, string outputSchema)
    {
        using var outputDocument = JsonDocument.Parse(content);
        using var schemaDocument = JsonDocument.Parse(outputSchema);
        ValidateElement(outputDocument.RootElement, schemaDocument.RootElement, "output");
    }

    private static void ValidateElement(JsonElement value, JsonElement schema, string path)
    {
        var expectedType = ReadSchemaType(schema, path);
        switch (expectedType)
        {
            case "object":
                ValidateObject(value, schema, path);
                break;
            case "array":
                ValidateArray(value, schema, path);
                break;
            case "string":
                ValidateString(value, schema, path);
                break;
            case "boolean":
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    throw Invalid(path, "boolean");
                }

                break;
            default:
                throw new InvalidOperationException($"Analysis output schema type '{expectedType}' at {path} is not supported.");
        }
    }

    private static void ValidateObject(JsonElement value, JsonElement schema, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(path, "object");
        }

        var hasProperties = TryGetObject(schema, "properties", out var properties);
        ValidateRequiredProperties(value, schema, path);
        if (IsAdditionalPropertiesFalse(schema))
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!hasProperties || !properties.TryGetProperty(property.Name, out _))
                {
                    throw new InvalidOperationException($"Analysis worker output contains unsupported property {path}.{property.Name}.");
                }
            }
        }

        if (!hasProperties)
        {
            return;
        }

        foreach (var propertySchema in properties.EnumerateObject())
        {
            if (value.TryGetProperty(propertySchema.Name, out var propertyValue))
            {
                ValidateElement(propertyValue, propertySchema.Value, path + "." + propertySchema.Name);
            }
        }
    }

    private static void ValidateArray(JsonElement value, JsonElement schema, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(path, "array");
        }

        if (!TryGetObject(schema, "items", out var itemSchema))
        {
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            ValidateElement(item, itemSchema, path + "[" + index + "]");
            index++;
        }
    }

    private static void ValidateString(JsonElement value, JsonElement schema, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(path, "string");
        }

        if (!schema.TryGetProperty("enum", out var enumElement) || enumElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var actual = value.GetString();
        foreach (var allowed in enumElement.EnumerateArray())
        {
            if (allowed.ValueKind == JsonValueKind.String && string.Equals(actual, allowed.GetString(), StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Analysis worker output value at {path} is not allowed by its output schema.");
    }

    private static void ValidateRequiredProperties(JsonElement value, JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !value.TryGetProperty(item.GetString()!, out _))
            {
                throw new InvalidOperationException($"Analysis worker output is missing required property {path}.{item.GetString()}.");
            }
        }
    }

    private static string ReadSchemaType(JsonElement schema, string path)
    {
        if (!schema.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(typeElement.GetString()))
        {
            throw new InvalidOperationException($"Analysis output schema is missing string type at {path}.");
        }

        return typeElement.GetString()!;
    }

    private static bool TryGetObject(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsAdditionalPropertiesFalse(JsonElement schema)
    {
        return schema.TryGetProperty("additionalProperties", out var value) &&
            value.ValueKind == JsonValueKind.False;
    }

    private static InvalidOperationException Invalid(string path, string expected)
    {
        return new InvalidOperationException($"Analysis worker output at {path} must be {expected}.");
    }
}
