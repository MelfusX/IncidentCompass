using IncidentCompass.Application.Intake.Artifacts;

namespace IncidentCompass.Infrastructure.Intake;

// No `triage_reports` table exists until Phase 5; this is the honest placeholder adapter behind
// the same port Phase 5 will implement for real.
internal sealed class NoPriorReportSummaryProvider : IPriorReportSummaryProvider
{
    public Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken) =>
        Task.FromResult<PriorReportSummary?>(null);
}
