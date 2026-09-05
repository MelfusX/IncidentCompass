namespace IncidentCompass.Application.Tickets;

public sealed record TicketSearchRequest(
    string Fingerprint,
    string ServiceName,
    string? Component,
    string? ErrorType,
    string? ErrorMessage,
    IReadOnlyList<string> KnownLabels);
