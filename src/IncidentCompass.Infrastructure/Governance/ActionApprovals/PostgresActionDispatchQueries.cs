using IncidentCompass.Application.Governance.ActionApprovals;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionDispatchQueries
{
    public static Task<IReadOnlyList<ActionDispatchCandidate>> FindClaimAsync(
        NpgsqlConnection connection,
        int limit,
        CancellationToken cancellationToken) =>
        FindAsync(
            connection,
            "a.state = 'approved' AND a.dispatch_started_at IS NULL",
            limit,
            cancellationToken);

    public static Task<IReadOnlyList<ActionDispatchCandidate>> FindExpiryAsync(
        NpgsqlConnection connection,
        int limit,
        CancellationToken cancellationToken) =>
        FindAsync(
            connection,
            "a.state = 'requested' AND a.expires_at_utc <= clock_timestamp()",
            limit,
            cancellationToken);

    public static Task<IReadOnlyList<ActionDispatchCandidate>> FindSupersededAsync(
        NpgsqlConnection connection,
        int limit,
        CancellationToken cancellationToken) =>
        FindAsync(
            connection,
            "a.state IN ('requested', 'approved') AND a.dispatch_started_at IS NULL " +
            "AND a.origin_report_id <> (SELECT r.id FROM incidentcompass.triage_reports r " +
            "WHERE r.fault_id = a.fault_id ORDER BY r.created_at_utc DESC, r.id DESC LIMIT 1)",
            limit,
            cancellationToken);

    public static Task<IReadOnlyList<ActionDispatchCandidate>> FindRecoveryAsync(
        NpgsqlConnection connection,
        int limit,
        CancellationToken cancellationToken) =>
        FindAsync(
            connection,
            "a.state = 'approved' AND a.dispatch_started_at IS NOT NULL " +
            "AND a.dispatch_deadline_at <= clock_timestamp()",
            limit,
            cancellationToken);

    private static async Task<IReadOnlyList<ActionDispatchCandidate>> FindAsync(
        NpgsqlConnection connection,
        string predicate,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT a.id, a.fault_id FROM incidentcompass.action_approvals a WHERE " + predicate +
            " ORDER BY a.created_at_utc, a.id LIMIT @limit;",
            connection);
        command.Parameters.AddWithValue("limit", limit);
        var rows = new List<ActionDispatchCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ActionDispatchCandidate(reader.GetGuid(0), reader.GetGuid(1)));
        }

        return rows;
    }
}
