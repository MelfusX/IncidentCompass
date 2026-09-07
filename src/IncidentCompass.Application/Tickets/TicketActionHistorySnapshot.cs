namespace IncidentCompass.Application.Tickets;

public sealed record TicketActionHistorySnapshot(
    byte[]? ConfirmedCanonicalResult,
    string? ConfirmedSummary,
    IReadOnlyList<string> OutcomeUnknownMarkers,
    bool HasPendingAction,
    bool HistoryLimitExceeded,
    bool HasUnsafeHistory);
