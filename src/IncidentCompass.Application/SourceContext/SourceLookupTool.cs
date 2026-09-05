using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.Validation;
using IncidentCompass.Domain.Governance;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.SourceContext;

internal sealed class SourceLookupTool(
    ISourceContextLookup sourceContextLookup,
    TimeProvider timeProvider) : IImmediateAgentTool
{
    public AiToolDefinition Definition { get; } = new(
        "source_lookup",
        "Inspect bounded source context for backend-selected stack frames in the current release.",
        "v1",
        CanonicalJsonSerializer.ToElement(new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject()
        }));

    public ToolValidationResult Validate(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Any())
        {
            return ToolValidationResult.Invalid(
                "invalid_arguments",
                "source_lookup accepts only an empty object.");
        }

        return ToolValidationResult.Valid(CanonicalJsonSerializer.ToElement(new JsonObject()));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement sanitizedArguments,
        CancellationToken cancellationToken)
    {
        if (!context.Configuration.CurrentReleases.TryGetValue(context.FaultServiceName, out var release) ||
            string.IsNullOrWhiteSpace(release))
        {
            return SuccessfulOutcome(SourceLookupResult.Unavailable("source_release_unavailable"), context, release: null);
        }

        if (context.TriggerSignal is null)
        {
            return SuccessfulOutcome(SourceLookupResult.Unavailable("source_signal_unavailable"), context, release);
        }

        var frames = SourceStackTraceExtractor.Extract(context.TriggerSignal);
        if (frames.Count == 0)
        {
            return SuccessfulOutcome(SourceLookupResult.NoMatch("source_frames_not_found"), context, release);
        }

        var result = await sourceContextLookup.LookupAsync(
            new SourceLookupRequest(context.FaultServiceName, release, frames),
            cancellationToken);
        return SuccessfulOutcome(result, context, release);
    }

    private ToolExecutionResult SuccessfulOutcome(
        SourceLookupResult result,
        AgentToolExecutionContext context,
        string? release)
    {
        var artifacts = result.Matches
            .Select(match => CreateArtifact(context, match))
            .ToArray();
        return new ToolExecutionResult(
            ToolExecutionStatus.Succeeded,
            CreateOutput(result, artifacts, release),
            Artifacts: artifacts);
    }

    private TriageArtifact CreateArtifact(
        AgentToolExecutionContext context,
        SourceLookupMatch match)
    {
        var payload = new JsonObject
        {
            ["evidenceKind"] = "SourceCode",
            ["relativePath"] = match.RelativePath,
            ["lineStart"] = match.LineStart,
            ["lineEnd"] = match.LineEnd,
            ["excerpt"] = match.Excerpt,
            ["release"] = match.Release,
            ["mappingMethod"] = match.MappingMethod
        };
        var canonical = CanonicalJsonSerializer.Canonicalize(payload);
        return new TriageArtifact(
            Guid.NewGuid(),
            context.Job.Id,
            context.Job.Attempt,
            ArtifactKind.RetrievedItem,
            $"source:{match.Release}:{match.RelativePath}",
            CanonicalJsonSerializer.ToElement(payload),
            CanonicalJsonSerializer.ComputeSha256Hex(canonical),
            timeProvider.GetUtcNow());
    }

    private static JsonElement CreateOutput(
        SourceLookupResult result,
        IReadOnlyList<TriageArtifact> artifacts,
        string? release)
    {
        var items = new JsonArray();
        for (var index = 0; index < result.Matches.Count; index++)
        {
            var match = result.Matches[index];
            items.Add(new JsonObject
            {
                ["artifactId"] = artifacts[index].Id.ToString(),
                ["title"] = match.RelativePath,
                ["relativePath"] = match.RelativePath,
                ["lineStart"] = match.LineStart,
                ["lineEnd"] = match.LineEnd,
                ["quote"] = match.Excerpt,
                ["release"] = match.Release,
                ["mappingMethod"] = match.MappingMethod
            });
        }

        var outcome = result.Outcome switch
        {
            SourceLookupOutcome.Matched => "matched",
            SourceLookupOutcome.NoMatch => "no_match",
            _ => "connector_unavailable"
        };
        var message = result.Outcome switch
        {
            SourceLookupOutcome.Matched => "matches found",
            SourceLookupOutcome.NoMatch => "no matches",
            _ => "connector unavailable"
        };
        return CanonicalJsonSerializer.ToElement(new JsonObject
        {
            ["matched"] = result.Outcome == SourceLookupOutcome.Matched && items.Count > 0,
            ["message"] = message,
            ["outcome"] = outcome,
            ["code"] = result.Code,
            ["release"] = release,
            ["items"] = items,
            ["limitations"] = new JsonArray(result.Limitations.Select(item => JsonValue.Create(item.Code)).ToArray()),
            ["noMatchReason"] = result.Outcome == SourceLookupOutcome.Matched ? null : result.Code
        });
    }
}
