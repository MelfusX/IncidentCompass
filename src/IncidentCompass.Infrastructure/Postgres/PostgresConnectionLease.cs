using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

/// <summary>
/// A scoped handle over the connection a repository call should use. It is a class, not a struct,
/// so a lease has exactly one owner: copying a disposable value type would produce two owners of
/// the same connection, and disposing a copy would leave the original open.
/// <para>
/// Ownership: a lease that owns its connection opened it for this call and closes it on dispose. A
/// borrowed lease carries the ambient intake transaction and disposes nothing, because
/// <c>PostgresIntakeUnitOfWork</c> owns both that connection and that transaction and is the only
/// place allowed to commit, roll back or dispose them. A lease therefore never disposes
/// <see cref="Transaction"/>: disposing it here would kill the caller's in-flight transaction.
/// </para>
/// </summary>
internal sealed class PostgresConnectionLease : IAsyncDisposable
{
    private readonly bool ownsConnection;
    private bool disposed;

    public PostgresConnectionLease(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool ownsConnection)
    {
        if (ownsConnection && transaction is not null)
        {
            throw new InvalidOperationException(
                "A connection-owning lease must not carry a transaction it does not own.");
        }

        Connection = connection;
        Transaction = transaction;
        this.ownsConnection = ownsConnection;
    }

    public NpgsqlConnection Connection { get; }

    public NpgsqlTransaction? Transaction { get; }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }
    }
}
