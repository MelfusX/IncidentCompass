using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionOriginContext
{
    public static async Task<GroundedActionProposalContext> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IncidentCompass.Application.Governance.ActionApprovals.ActionApprovalRecord action,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT config_hash FROM incidentcompass.triage_jobs WHERE id = @job_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("job_id", action.JobId);
        var configHash = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new GroundedActionProposalContext(
            action.FaultId, action.JobId, action.Attempt, configHash, []);
    }
}
