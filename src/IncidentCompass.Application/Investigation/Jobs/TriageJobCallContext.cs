using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed record TriageJobCallContext(
    TriageJob Job,
    TriageConfiguration Configuration,
    DateTimeOffset AttemptStartedAtUtc,
    string RouteId,
    string CallKind,
    string? Role = null);
