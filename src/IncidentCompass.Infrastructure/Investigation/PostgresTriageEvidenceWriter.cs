using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal static class PostgresTriageEvidenceWriter
{
    public static async Task ReplaceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reportId,
        IReadOnlyList<GroundedReportEvidence> evidence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Defensive replace-on-retry guard: unreachable by construction because a committed report
        // also commits job Succeeded, and the job-attempt fence blocks any retry from reaching here.
        await DeleteAsync(connection, transaction, reportId, cancellationToken);
        foreach (var item in evidence)
        {
            await InsertAsync(connection, transaction, reportId, item, now, cancellationToken);
        }
    }

    private static async Task DeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM incidentcompass.triage_evidence WHERE report_id = @report_id;",
            connection,
            transaction);
        AddParameter(command, "report_id", reportId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reportId,
        GroundedReportEvidence item,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_evidence (
                id, report_id, kind, artifact_id, reference, quote, score, created_at_utc)
            VALUES (
                @id, @report_id, @kind, @artifact_id, @reference, @quote, @score, @created_at_utc);
            """, connection, transaction);
        AddParameter(command, "id", Guid.NewGuid());
        AddParameter(command, "report_id", reportId);
        AddParameter(command, "kind", item.Kind);
        AddParameter(command, "artifact_id", item.ArtifactId);
        AddParameter(command, "reference", item.Reference);
        AddParameter(command, "quote", item.Quote);
        AddParameter(command, "score", item.Score);
        AddParameter(command, "created_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
