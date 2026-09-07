using Npgsql;

namespace IncidentCompass.IntegrationTests;

internal sealed class MigrationDatabase(
    string connectionString,
    string databaseName,
    string rootConnectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<MigrationDatabase> CreateAsync(PostgresRepositoryFixture fixture)
    {
        var rootConnectionString = await fixture.GetConnectionStringAsync();
        var databaseName = "migration_" + Guid.NewGuid().ToString("N");
        var databaseConnectionString = new NpgsqlConnectionStringBuilder(rootConnectionString)
        {
            Database = databaseName
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(rootConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName};", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        return new MigrationDatabase(databaseConnectionString, databaseName, rootConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        var rootBuilder = new NpgsqlConnectionStringBuilder(rootConnectionString);
        await using var connection = new NpgsqlConnection(rootBuilder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
