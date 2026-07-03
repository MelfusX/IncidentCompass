using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class TriageReportPublisher(ITriageReportRepository reportRepository)
{
    public async Task PublishAsync(
        TriageJob job,
        string workerId,
        AiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var report = TriageReportParser.Parse(toolCall.Arguments);
        await reportRepository.PublishAsync(job, workerId, report, cancellationToken);
    }
}
