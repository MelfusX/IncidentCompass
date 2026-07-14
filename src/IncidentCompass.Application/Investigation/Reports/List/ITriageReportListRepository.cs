namespace IncidentCompass.Application.Investigation.Reports.List;

public interface ITriageReportListRepository
{
    Task<IReadOnlyList<TriageReportListItemResponse>> ListAsync(
        TriageReportListFilter filter,
        string tenantId,
        CancellationToken cancellationToken);
}