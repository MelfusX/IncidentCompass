namespace IncidentCompass.Application.Intake.Configuration;

public sealed record TriageToolSettings(
    string Kind,
    string? EmbeddingRouteId,
    int? TopK,
    double? MinScore,
    string? Category = null,
    string? LogicalTargetId = null,
    string? Mode = null);
