namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageToolSettings(
    string Kind,
    string? EmbeddingRouteId,
    int? TopK,
    double? MinScore);
