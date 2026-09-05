using System.Text.Json;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Tickets;

internal static class TicketSearchContextExtractor
{
    private const int MaxTermCharacters = 128;
    private const int MaxMessageCharacters = 4096;
    private const int MaxLabels = 10;
    private const int MaxLabelCharacters = 64;

    public static TicketSearchRequest Create(
        string fingerprint,
        string serviceName,
        Signal signal) =>
        new(
            Bound(fingerprint, MaxTermCharacters),
            Bound(serviceName, MaxTermCharacters),
            ReadComponent(signal.Attributes),
            BoundNullable(signal.ErrorType, MaxTermCharacters),
            BoundNullable(signal.ErrorMessage, MaxMessageCharacters),
            ReadLabels(signal.Attributes));

    private static string? ReadComponent(JsonElement attributes)
    {
        foreach (var name in new[] { "service.component", "component", "code.namespace" })
        {
            var value = ReadString(attributes, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Bound(value, MaxTermCharacters);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement attributes)
    {
        if (attributes.ValueKind != JsonValueKind.Object ||
            !attributes.TryGetProperty("incident.labels", out var labels) ||
            labels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return labels.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => Bound(item!, MaxLabelCharacters))
            .Where(static item => item.Length > 0)
            .Take(MaxLabels)
            .ToArray();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Bound(string value, int maximum) =>
        TicketTextBounds.Bound(value, maximum, trim: true);

    private static string? BoundNullable(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value, maximum);
}
