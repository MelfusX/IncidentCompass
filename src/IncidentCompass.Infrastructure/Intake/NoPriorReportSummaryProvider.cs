using IncidentCompass.Application.Intake.Artifacts;

namespace IncidentCompass.Infrastructure.Intake;

// Phase 2 writes a minimal `triage_reports` row, but recurrence grounding still needs
// the richer report-summary lookup that arrives with the Phase 5 report pipeline.
internal sealed class NoPriorReportSummaryProvider : IPriorReportSummaryProvider
{
    public Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken) =>
        Task.FromResult<PriorReportSummary?>(null);
}
