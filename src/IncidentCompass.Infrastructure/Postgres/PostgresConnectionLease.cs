using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal readonly struct PostgresConnectionLease(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool ownsConnection)
    : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = connection;

    public NpgsqlTransaction? Transaction { get; } = transaction;

    public async ValueTask DisposeAsync()
    {
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }
    }
}
