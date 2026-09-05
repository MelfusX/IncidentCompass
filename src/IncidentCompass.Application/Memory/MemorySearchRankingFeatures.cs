namespace IncidentCompass.Application.Memory;

internal sealed record MemorySearchRankingFeatures(
    double CombinedScore,
    double VectorScore,
    MemoryDocumentationStatus DocumentationStatus,
    double DocumentationBoost,
    double LexicalBoost,
    double ComponentBoost,
    double EvidenceKindBoost);
