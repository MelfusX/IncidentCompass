using System.Globalization;
using System.Text.Json.Nodes;
using Google.Protobuf;
using OpenTelemetry.Proto.Common.V1;

namespace IncidentCompass.Api;

internal static class OtlpAttributeMapper
{
    private static readonly DateTimeOffset UnixEpoch = DateTimeOffset.UnixEpoch;

    public static JsonObject ToJsonObject(IEnumerable<KeyValue> attributes)
    {
        var output = new JsonObject();
        foreach (var attribute in attributes)
        {
            output[attribute.Key] = ToJsonNode(attribute.Value);
        }

        return output;
    }

    public static string? GetString(JsonObject attributes, params string[] names)
    {
        foreach (var name in names)
        {
            if (attributes.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
            {
                return text;
            }
        }

        return null;
    }

    public static int? GetInt(JsonObject attributes, params string[] names)
    {
        foreach (var name in names)
        {
            if (!attributes.TryGetPropertyValue(name, out var value) || value is not JsonValue jsonValue)
            {
                continue;
            }

            if (jsonValue.TryGetValue<long>(out var longValue) && longValue is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longValue;
            }

            if (jsonValue.TryGetValue<string>(out var text) && int.TryParse(text, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static DateTimeOffset? ToDateTimeOffset(ulong unixNanoseconds)
    {
        if (unixNanoseconds == 0 || unixNanoseconds / 100 > long.MaxValue)
        {
            return null;
        }

        try
        {
            return UnixEpoch.AddTicks((long)(unixNanoseconds / 100));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static string? ToHex(ByteString value) => value.IsEmpty ? null : Convert.ToHexString(value.Span).ToLowerInvariant();

    public static JsonNode? ToJsonNode(AnyValue value) => value.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => JsonValue.Create(value.StringValue),
        AnyValue.ValueOneofCase.BoolValue => JsonValue.Create(value.BoolValue),
        AnyValue.ValueOneofCase.IntValue => JsonValue.Create(value.IntValue),
        AnyValue.ValueOneofCase.DoubleValue => JsonValue.Create(value.DoubleValue),
        AnyValue.ValueOneofCase.BytesValue => JsonValue.Create(Convert.ToBase64String(value.BytesValue.ToByteArray())),
        AnyValue.ValueOneofCase.ArrayValue => new JsonArray(value.ArrayValue.Values.Select(ToJsonNode).ToArray()),
        AnyValue.ValueOneofCase.KvlistValue => ToJsonObject(value.KvlistValue.Values),
        _ => null
    };
}