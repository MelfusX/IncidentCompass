using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresPriorReportSummaryProvider(PostgresDataSourceProvider dataSourceProvider)
    : IPriorReportSummaryProvider
{
    public async Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, summary, limitations
            FROM incidentcompass.triage_reports
            WHERE fault_id = @fault_id
            ORDER BY created_at_utc DESC, id DESC
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("fault_id", faultId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PriorReportSummary(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetFieldValue<string[]>(2));
    }
}
