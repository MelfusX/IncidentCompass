using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Configuration;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.Application.Intake.Redaction;

/// <summary>
/// Everything one redaction pass needs: the configuration snapshot, its compiled patterns, the
/// optional canonical-pseudonym verifier, the optional logger and the root used to describe field
/// paths in diagnostics. Field paths are used for logging only; values never leave the redactor.
/// </summary>
internal sealed record RedactionContext(
    RedactionSettings Settings,
    CompiledRedactionPatterns Patterns,
    Func<JsonNode?, string, bool>? CanonicalPseudonymVerifier,
    ILogger? Logger,
    string PathRoot)
{
    public static RedactionContext Create(
        RedactionSettings settings,
        Func<JsonNode?, string, bool>? canonicalPseudonymVerifier,
        ILogger? logger,
        string pathRoot) =>
        new(
            settings,
            CompiledRedactionPatterns.ForSettings(settings),
            canonicalPseudonymVerifier,
            logger,
            pathRoot);

    public RedactionContext WithPathRoot(string pathRoot) => this with { PathRoot = pathRoot };

    public string DescribeField(string path) =>
        string.IsNullOrEmpty(path) ? PathRoot : PathRoot + "." + path;
}
