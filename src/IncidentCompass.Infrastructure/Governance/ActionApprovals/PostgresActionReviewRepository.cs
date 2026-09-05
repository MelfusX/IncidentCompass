using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Postgres;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionReviewRepository(
    PostgresDataSourceProvider dataSourceProvider,
    IActionApprovalTransactionFaultInjector faultInjector,
    TimeProvider timeProvider) : IActionApprovalReviewRepository
{
    private readonly PostgresActionDecisionTransaction decisionTransaction = new(faultInjector, timeProvider);

    public Task<IReadOnlyList<ActionApprovalRecord>> ListAsync(
        ActionApprovalListFilter filter,
        string tenantId,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "list action approvals",
            async () =>
            {
                await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
                return await PostgresActionApprovalQueries.ListAsync(connection, filter, tenantId, cancellationToken);
            });

    public Task<(ActionApprovalRecord Action, IReadOnlyList<ActionApprovalProvenance> Provenance)?> FindAsync(
        Guid actionId,
        string tenantId,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "read action approval",
            () => FindCoreAsync(actionId, tenantId, cancellationToken));

    public Task<ActionDecisionResult> DecideAsync(
        ActionDecisionRequest request,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "decide action approval",
            () => DecideTransactionAsync(request, cancellationToken));

    private async Task<ActionDecisionResult> DecideTransactionAsync(
        ActionDecisionRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await decisionTransaction.ExecuteAsync(connection, transaction, request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<(ActionApprovalRecord Action, IReadOnlyList<ActionApprovalProvenance> Provenance)?> FindCoreAsync(
        Guid actionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        var action = await PostgresActionApprovalQueries.FindAsync(
            connection, null, actionId, tenantId, false, cancellationToken);
        if (action is null)
        {
            return null;
        }

        var provenance = await PostgresActionApprovalQueries.ReadProvenanceAsync(
            connection, null, action.Id, cancellationToken);
        return (action, provenance);
    }
}
