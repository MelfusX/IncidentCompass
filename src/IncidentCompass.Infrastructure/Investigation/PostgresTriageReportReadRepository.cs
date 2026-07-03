using System.Text.Json;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Application.Investigation.Reports.Get;
using IncidentCompass.Infrastructure.Intake;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresTriageReportReadRepository(PostgresDataSourceProvider dataSourceProvider)
    : ITriageReportReadRepository
{
    public async Task<TriageReportDetailsResponse?> FindByIdAsync(Guid reportId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        var report = await LoadReportAsync(connection, reportId, cancellationToken);
        if (report is null)
        {
            return null;
        }

        var evidence = await LoadEvidenceAsync(connection, reportId, cancellationToken);
        return report with { Evidence = evidence };
    }

    private static async Task<TriageReportDetailsResponse?> LoadReportAsync(
        NpgsqlConnection connection,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, fault_id, status, summary, classification, confidence, is_mass_issue,
                   recommended_next_action, limitations, config_hash, created_at_utc
            FROM incidentcompass.triage_reports
            WHERE id = @report_id;
            """, connection);
        command.Parameters.AddWithValue("report_id", reportId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TriageReportDetailsResponse(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetBoolean(6),
            reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            reader.GetFieldValue<string[]>(8),
            reader.GetString(9),
            PostgresTriageJobMapper.GetDateTimeOffset(reader, 10),
            []);
    }

    private static async Task<IReadOnlyList<TriageReportEvidenceResponse>> LoadEvidenceAsync(
        NpgsqlConnection connection,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT e.id, e.kind, e.artifact_id, e.reference, e.quote, e.score,
                   a.kind, a.domain_ref, a.redacted_payload::text
            FROM incidentcompass.triage_evidence e
            JOIN incidentcompass.triage_artifacts a ON a.id = e.artifact_id
            WHERE e.report_id = @report_id
            ORDER BY e.created_at_utc, e.id;
            """, connection);
        command.Parameters.AddWithValue("report_id", reportId);

        var evidence = new List<TriageReportEvidenceResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var payload = JsonDocument.Parse(reader.GetString(8));
            evidence.Add(new TriageReportEvidenceResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                payload.RootElement.Clone()));
        }

        return evidence;
    }
}
