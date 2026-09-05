namespace IncidentCompass.Infrastructure.Observability;

internal sealed record ModelCallUsage(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    int TotalTokens);
