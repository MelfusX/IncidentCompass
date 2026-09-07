namespace IncidentCompass.Application.Tickets;

public sealed record TicketUpdateEvidence(
    Guid ArtifactId,
    string TicketId);
