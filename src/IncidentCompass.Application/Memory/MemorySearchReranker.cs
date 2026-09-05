using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Memory;

internal static class MemorySearchReranker
{
    private const double CurrentDocumentationBoost = 10.0;
    private const double StaleDocumentationBoost = 5.0;
    private const double UnversionedDocumentationBoost = 2.5;
    private const double ComponentBoost = 0.25;
    private const double EvidenceKindBoost = 0.2;

    private static readonly Dictionary<string, string[]> EvidenceKindAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["runbook"] = ["runbook", "playbook"],
            ["known_incident"] = ["known incident"],
            ["operational_note"] = ["operational note"],
            ["release_note"] = ["release note"],
            ["postmortem"] = ["postmortem"]
        };

    public static IReadOnlyList<MemorySearchMatch> Rank(
        string query,
        TriageConfiguration configuration,
        string faultServiceName,
        IReadOnlyList<MemorySearchMatch> candidates,
        int topK)
    {
        return MemorySearchLexicalFilter.ApplyForReranking(query, candidates)
            .Select(match => (
                Match: match,
                Features: CreateFeatures(query, configuration, faultServiceName, match)))
            .OrderByDescending(static ranked => ranked.Features.CombinedScore)
            .ThenByDescending(static ranked => ranked.Features.VectorScore)
            .ThenBy(static ranked => ranked.Match.ChunkId)
            .Take(topK)
            .Select(static ranked => ranked.Match)
            .ToArray();
    }

    internal static MemorySearchRankingFeatures CreateFeatures(
        string query,
        TriageConfiguration configuration,
        string faultServiceName,
        MemorySearchMatch match)
    {
        var documentation = MemoryDocumentationStatusEvaluator.Assess(
            configuration,
            faultServiceName,
            match);
        var documentationBoost = documentation.Status switch
        {
            MemoryDocumentationStatus.Current => CurrentDocumentationBoost,
            MemoryDocumentationStatus.Stale => StaleDocumentationBoost,
            MemoryDocumentationStatus.Unversioned => UnversionedDocumentationBoost,
            _ => 0
        };
        var lexicalBoost = MemorySearchLexicalFilter.Coverage(query, match.Text);
        var componentBoost = !string.IsNullOrWhiteSpace(match.Component) &&
            MemorySearchLexicalFilter.ContainsNormalizedTokenOrPhrase(query, match.Component)
            ? ComponentBoost
            : 0;
        var evidenceKindBoost = HasEvidenceKindAlias(query, match.Kind)
            ? EvidenceKindBoost
            : 0;

        return new MemorySearchRankingFeatures(
            match.Score + documentationBoost + lexicalBoost + componentBoost + evidenceKindBoost,
            match.Score,
            documentation.Status,
            documentationBoost,
            lexicalBoost,
            componentBoost,
            evidenceKindBoost);
    }

    private static bool HasEvidenceKindAlias(string query, string kind)
    {
        return EvidenceKindAliases.TryGetValue(kind, out var aliases) &&
            aliases.Any(alias => MemorySearchLexicalFilter.ContainsNormalizedTokenOrPhrase(query, alias));
    }

}
