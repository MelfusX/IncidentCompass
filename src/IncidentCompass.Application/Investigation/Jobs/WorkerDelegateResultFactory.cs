using System.Text.Json;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class WorkerDelegateResultFactory
{
    private const string MemoryRoleName = "memory";

    public static WorkerDelegateResult Create(
        string roleName,
        string workerContent,
        Guid workerOutputArtifactId)
    {
        using var document = JsonDocument.Parse(workerContent);
        return IsMemoryOutput(roleName, document.RootElement)
            ? CreateMemoryResult(roleName, workerContent, workerOutputArtifactId)
            : CreateAnalysisResult(roleName, workerContent, workerOutputArtifactId);
    }

    private static bool IsMemoryOutput(string roleName, JsonElement root)
    {
        return string.Equals(roleName, MemoryRoleName, StringComparison.Ordinal) ||
            root.TryGetProperty("matched", out _);
    }

    private static WorkerDelegateResult CreateAnalysisResult(
        string roleName,
        string workerContent,
        Guid workerOutputArtifactId)
    {
        var output = AnalysisWorkerOutputParser.Parse(workerContent, roleName);
        var rationale = output.Rationale ?? string.Join(" ", output.KeyFacts);
        return new WorkerDelegateResult(
            rationale,
            JsonSerializer.Serialize(new
            {
                role = roleName,
                summary = rationale,
                keyFacts = output.KeyFacts,
                candidateClassification = output.CandidateClassification,
                needsDeeperContext = output.NeedsDeeperContext,
                artifactId = workerOutputArtifactId
            }));
    }

    private static WorkerDelegateResult CreateMemoryResult(
        string roleName,
        string workerContent,
        Guid workerOutputArtifactId)
    {
        var output = MemoryWorkerOutputParser.Parse(workerContent);
        return new WorkerDelegateResult(
            output.Rationale,
            JsonSerializer.Serialize(new
            {
                role = roleName,
                summary = output.Rationale,
                matched = output.Matched,
                items = output.Items,
                noMatchReason = output.NoMatchReason,
                artifactId = workerOutputArtifactId
            }));
    }
}