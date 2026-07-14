using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed record RecurrenceAttachmentResult(TriageJob Job, RecurrenceState State);