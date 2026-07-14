using IncidentCompass.Application.Investigation.Reports.List;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresTriageReportListRepository(PostgresDataSourceProvider dataSourceProvider)
    : ITriageReportListRepository
{
    public Task<IReadOnlyList<TriageReportListItemResponse>> ListAsync(
        TriageReportListFilter filter,
        string tenantId,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "list triage reports",
            () => ListCoreAsync(filter, tenantId, cancellationToken));

    private async Task<IReadOnlyList<TriageReportListItemResponse>> ListCoreAsync(
        TriageReportListFilter filter,
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT r.id, r.fault_id, r.status, r.summary, r.classification, r.confidence,
                   r.is_mass_issue, r.recommended_next_action, r.created_at_utc,
                   f.service_name, f.environment, r.supersedes_report_id, successor.id
            FROM incidentcompass.triage_reports r
            JOIN incidentcompass.faults f ON f.id = r.fault_id
            LEFT JOIN LATERAL (
                SELECT candidate.id
                FROM incidentcompass.triage_reports candidate
                WHERE candidate.supersedes_report_id = r.id
                ORDER BY candidate.created_at_utc DESC, candidate.id DESC
                LIMIT 1
            ) successor ON TRUE
            WHERE f.tenant_id = @tenant_id
              AND (@fault_id::uuid IS NULL OR r.fault_id = @fault_id)
              AND (@service_name::text IS NULL OR f.service_name = @service_name)
              AND (@environment::text IS NULL OR f.environment = @environment)
              AND (@status::text IS NULL OR r.status = @status)
              AND (@classification::text IS NULL OR r.classification = @classification)
              AND (
                  @before_created_at_utc::timestamptz IS NULL
                  OR r.created_at_utc < @before_created_at_utc
                  OR (r.created_at_utc = @before_created_at_utc AND r.id < @before_report_id::uuid)
              )
            ORDER BY r.created_at_utc DESC, r.id DESC
            LIMIT @limit;
            """, connection);
        command.AddParameter("tenant_id", tenantId);
        command.AddParameter("fault_id", filter.FaultId);
        command.AddParameter("service_name", filter.ServiceName);
        command.AddParameter("environment", filter.Environment);
        command.AddParameter("status", filter.Status);
        command.AddParameter("classification", filter.Classification);
        command.AddParameter("before_created_at_utc", filter.BeforeCreatedAtUtc);
        command.AddParameter("before_report_id", filter.BeforeReportId);
        command.AddParameter("limit", filter.Limit);

        var reports = new List<TriageReportListItemResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Guid? supersededByReportId = reader.IsDBNull(12) ? null : reader.GetGuid(12);
            reports.Add(new TriageReportListItemResponse(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetBoolean(6),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7), reader.GetDateTimeOffset(8),
                reader.GetString(9), reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetGuid(11),
                supersededByReportId, supersededByReportId is null));
        }

        return reports;
    }
}