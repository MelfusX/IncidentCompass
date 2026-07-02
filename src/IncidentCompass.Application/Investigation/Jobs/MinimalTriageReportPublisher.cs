using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class MinimalTriageReportPublisher(
    IMinimalTriageReportRepository reportRepository,
    TriageLedgerAppender ledgerAppender)
{
    public async Task PublishAsync(
        TriageJob job,
        string workerId,
        AiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var report = MinimalTriageReportParser.Parse(toolCall.Arguments);
        var reportId = await reportRepository.PublishAsync(job, workerId, report, cancellationToken);
        await ledgerAppender.AppendAsync(
            job,
            TriageLedgerEventType.ReportPublished,
            role: null,
            toolName: "publish_report",
            rationale: report.Summary,
            payloadRef: $"report:{reportId}",
            cancellationToken);
    }
}
