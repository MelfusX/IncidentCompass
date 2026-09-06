using IncidentCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class PostgresDataSourceProvider : IDisposable
{
    private readonly IOptions<PostgresConnectionOptions> options;
    private readonly Lazy<NpgsqlDataSource> dataSource;

    public PostgresDataSourceProvider(IOptions<PostgresConnectionOptions> options)
    {
        this.options = options;
        dataSource = new Lazy<NpgsqlDataSource>(
            CreateDataSource,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "open PostgreSQL connection",
            () => OpenConnectionCoreAsync(cancellationToken));

    private async Task<NpgsqlConnection> OpenConnectionCoreAsync(CancellationToken cancellationToken)
    {
        return await dataSource.Value.OpenConnectionAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (dataSource.IsValueCreated)
        {
            dataSource.Value.Dispose();
        }
    }

    private NpgsqlDataSource CreateDataSource()
    {
        var connectionOptions = options.Value;
        var connectionStringName = connectionOptions.ConnectionStringName;
        if (string.IsNullOrWhiteSpace(connectionOptions.ConnectionString))
        {
            throw PostgresConnectionConfigurationException.Missing(connectionStringName);
        }

        try
        {
            var builder = new NpgsqlDataSourceBuilder(connectionOptions.ConnectionString);
            builder.UseVector();
            return builder.Build();
        }
        catch (Exception exception) when (IsConnectionConfigurationException(exception))
        {
            throw PostgresConnectionConfigurationException.Invalid(connectionStringName);
        }
    }

    private static bool IsConnectionConfigurationException(Exception exception)
    {
        return exception is ArgumentException or InvalidOperationException or NotSupportedException;
    }
}
