using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Infrastructure.Postgres;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresIntakeUnitOfWork(
    PostgresDataSourceProvider dataSourceProvider,
    PostgresIntakeTransactionContext transactionContext) : IIntakeUnitOfWork
{
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "execute intake transaction",
            () => ExecuteCoreAsync(operation, cancellationToken));

    private async Task<TResult> ExecuteCoreAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (transactionContext.HasCurrent)
        {
            return await operation(cancellationToken);
        }

        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        transactionContext.Set(connection, transaction);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            transactionContext.Clear();
        }
    }
}
