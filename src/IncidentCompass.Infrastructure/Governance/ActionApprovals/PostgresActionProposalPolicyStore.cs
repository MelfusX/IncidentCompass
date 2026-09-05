using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Infrastructure.Postgres;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionProposalPolicyStore(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider)
{
    public Task<ActionProposalOrigin?> FindSafeOriginAsync(
        string tenantId,
        Guid originReportId,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "resolve safe action proposal origin",
            async () =>
            {
                await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
                return await PostgresActionSafeOriginReader.FindAsync(
                    connection, null, tenantId, originReportId, false, cancellationToken);
            });

    public Task<bool> RecordDenialAsync(
        string tenantId,
        Guid originReportId,
        string? auditedToolId,
        string reasonCode,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "record denied action proposal",
            () => RecordDenialTransactionAsync(
                tenantId, originReportId, auditedToolId, reasonCode, cancellationToken));

    private async Task<bool> RecordDenialTransactionAsync(
        string tenantId,
        Guid originReportId,
        string? auditedToolId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        ValidateInput(auditedToolId, reasonCode);
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var candidate = await PostgresActionSafeOriginReader.FindAsync(
                connection, transaction, tenantId, originReportId, false, cancellationToken);
            if (candidate is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            await PostgresFaultTransactionLock.LockAsync(
                connection, transaction, candidate.Job.FaultId, cancellationToken);
            var locked = await PostgresActionSafeOriginReader.FindAsync(
                connection, transaction, tenantId, originReportId, true, cancellationToken);
            if (locked is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }

            var origin = new GroundedActionProposalContext(
                locked.Job.FaultId, locked.Job.Id, locked.Job.Attempt, locked.Job.ConfigHash, []);
            await PostgresActionDenialWriter.InsertAsync(
                connection, transaction, origin, originReportId, auditedToolId,
                reasonCode, timeProvider.GetUtcNow(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidateInput(string? toolId, string reasonCode)
    {
        if ((toolId is not null && !AgentToolIdentity.IsValid(toolId)) ||
            reasonCode.Length is < 1 or > 128 || !reasonCode.All(IsSafeCharacter))
        {
            throw new ArgumentException("Action proposal denial input is invalid.");
        }
    }

    private static bool IsSafeCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.';
}
