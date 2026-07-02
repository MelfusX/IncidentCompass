using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresIntakeTransactionContext
{
    private NpgsqlConnection? connection;
    private NpgsqlTransaction? transaction;

    public bool HasCurrent => connection is not null;

    public async Task<PostgresConnectionLease> OpenConnectionAsync(
        PostgresDataSourceProvider dataSourceProvider,
        CancellationToken cancellationToken)
    {
        if (connection is not null)
        {
            return new PostgresConnectionLease(connection, transaction, ownsConnection: false);
        }

        var ownedConnection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        return new PostgresConnectionLease(ownedConnection, transaction: null, ownsConnection: true);
    }

    public void Set(NpgsqlConnection currentConnection, NpgsqlTransaction currentTransaction)
    {
        if (connection is not null)
        {
            throw new InvalidOperationException("An intake transaction is already active in this scope.");
        }

        connection = currentConnection;
        transaction = currentTransaction;
    }

    public void Clear()
    {
        connection = null;
        transaction = null;
    }
}
