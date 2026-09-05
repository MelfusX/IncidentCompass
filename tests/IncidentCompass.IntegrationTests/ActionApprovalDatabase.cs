using Npgsql;

namespace IncidentCompass.IntegrationTests;

internal sealed class ActionApprovalDatabase(
    string connectionString,
    string rootConnectionString,
    string databaseName) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public static async Task<ActionApprovalDatabase> CreateAsync(PostgresRepositoryFixture fixture)
    {
        var rootConnectionString = await fixture.GetConnectionStringAsync();
        var databaseName = "action_approval_" + Guid.NewGuid().ToString("N");
        var connectionString = new NpgsqlConnectionStringBuilder(rootConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(rootConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName};", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        return new ActionApprovalDatabase(connectionString, rootConnectionString, databaseName);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(rootConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
