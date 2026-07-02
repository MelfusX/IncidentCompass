using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.IngestSignal;
using IncidentCompass.Domain.Exceptions;

namespace IncidentCompass.Application.Intake.Normalization;

internal sealed class UserReportSignalNormalizer : ISignalNormalizer
{
    public IReadOnlyCollection<string> SourceKinds { get; } = [SignalSourceKinds.User, SignalSourceKinds.Manual];

    public NormalizedSignal Normalize(IngestSignalCommand command, DateTimeOffset receivedAtUtc)
    {
        var serviceName = string.IsNullOrWhiteSpace(command.ServiceName) ? "unknown" : command.ServiceName;
        var environment = string.IsNullOrWhiteSpace(command.Environment) ? "unknown" : command.Environment;

        var summary = ResolveSummary(command);

        return new NormalizedSignal(
            Source: command.SourceKind,
            ExternalId: command.ExternalId,
            TraceId: command.TraceId,
            SpanId: command.SpanId,
            ParentSpanId: null,
            ServiceName: serviceName,
            Environment: environment,
            OperationName: null,
            Severity: command.Severity,
            ErrorType: null,
            ErrorMessage: null,
            Summary: summary,
            Description: command.Description,
            HttpMethod: null,
            HttpRoute: null,
            HttpStatusCode: null,
            DurationMs: null,
            Attributes: command.Attributes ?? new JsonObject(),
            Body: command.Payload ?? new JsonObject(),
            ObservedAtUtc: command.ObservedAtUtc?.ToUniversalTime() ?? receivedAtUtc);
    }

    private static string ResolveSummary(IngestSignalCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.Summary))
        {
            return command.Summary;
        }

        if (!string.IsNullOrWhiteSpace(command.Description))
        {
            return command.Description.Length > 500
                ? command.Description[..497] + "..."
                : command.Description;
        }

        // Unreachable given upfront validation: IngestSignalCommandValidator requires at
        // least one of Summary/Description to be non-blank for user/manual sources.
        throw new InvariantViolationException(
            "A user or manual signal reached normalization with both Summary and Description blank; " +
            "this should be unreachable because IngestSignalCommandValidator enforces this upfront.");
    }
}
