namespace IncidentCompass.Application.Tickets;

public sealed record TicketSearchMatch(
    string Provider,
    string Scope,
    string ExternalId,
    string Title,
    string Status,
    string? Assignee,
    DateTimeOffset CreatedAtUtc,
    string Url,
    double Score);
