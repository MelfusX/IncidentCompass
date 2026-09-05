using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Application.Investigation.Reports.Context;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class TriageReportPublisher(
    ITriageReportRepository reportRepository,
    IReadOnlyContextOutcomeRepository contextOutcomeRepository)
{
    public async Task PublishAsync(
        TriageJob job,
        string workerId,
        AiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var report = TriageReportParser.Parse(toolCall.Arguments);
        var contextOutcomes = await contextOutcomeRepository.ReadCurrentAttemptAsync(
            job.Id,
            job.Attempt,
            cancellationToken);
        report = ContextOutcomeReportPolicy.Apply(report, contextOutcomes);
        await reportRepository.PublishAsync(job, workerId, report, cancellationToken);
    }
}
