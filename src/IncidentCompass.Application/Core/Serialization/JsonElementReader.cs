using System.Text.Json;

namespace IncidentCompass.Application.Core.Serialization;

internal static class JsonElementReader
{
    public static JsonElement RequireObject(
        JsonElement element,
        string message,
        Func<string, Exception> createException)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw createException(message);
        }

        return element;
    }

    public static string ReadRequiredString(
        JsonElement root,
        string propertyName,
        string missingMessage,
        Func<string, Exception> createException,
        string? nonStringMessage = null,
        bool trim = true)
    {
        var value = ReadOptionalString(root, propertyName, createException, nonStringMessage);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw createException(missingMessage);
        }

        return trim ? value.Trim() : value;
    }

    public static string? ReadOptionalString(
        JsonElement root,
        string propertyName,
        Func<string, Exception> createException,
        string? nonStringMessage = null)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            if (nonStringMessage is not null)
            {
                throw createException(nonStringMessage);
            }

            return null;
        }

        return element.GetString();
    }
}
