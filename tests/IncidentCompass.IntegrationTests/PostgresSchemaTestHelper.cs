using System.Runtime.CompilerServices;
using Npgsql;
using Testcontainers.PostgreSql;

namespace IncidentCompass.IntegrationTests;

internal static class PostgresSchemaTestHelper
{
    public static async Task EnsureSchemaAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);

        foreach (var scriptPath in Directory
                     .GetFiles(FindInitScriptDirectory(), "*.sql")
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            await ExecuteScriptAsync(connection, scriptPath);
        }
    }

    public static Task ApplyInitScriptsAsync(
        string connectionString,
        params string[] scriptNames) =>
        ApplyScriptsAsync(connectionString, FindInitScriptDirectory(), scriptNames);

    public static Task ApplyReleasedV011ScriptsAsync(
        string connectionString,
        params string[] scriptNames) =>
        ApplyScriptsAsync(connectionString, FindReleasedV011FixtureDirectory(), scriptNames);

    private static async Task ApplyScriptsAsync(
        string connectionString,
        string scriptDirectory,
        IReadOnlyList<string> scriptNames)
    {
        await using var connection = await OpenAsync(connectionString);

        foreach (var scriptName in scriptNames)
        {
            await ExecuteScriptAsync(connection, Path.Combine(scriptDirectory, scriptName));
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteScriptAsync(
        NpgsqlConnection connection,
        string scriptPath)
    {
        var schemaSql = await File.ReadAllTextAsync(scriptPath);
        await using var command = new NpgsqlCommand(schemaSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string FindReleasedV011FixtureDirectory()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Postgres",
            "v0.1.1");
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                "Released v0.1.1 PostgreSQL fixture directory was not found.");
        }

        return directory;
    }

    private static string FindInitScriptDirectory()
    {
        foreach (var startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "infra",
                    "postgres",
                    "init");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("PostgreSQL init script directory was not found.");
    }
}

[CollectionDefinition("PostgreSQL repository", DisableParallelization = true)]
public sealed class PostgresRepositoryCollection
    : ICollectionFixture<PostgresRepositoryFixture>
{
    public const string CollectionName = "PostgreSQL repository";
}

public sealed class PostgresRepositoryFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private bool started;

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (started)
        {
            await container!.DisposeAsync();
        }
    }

    public async Task<string> GetConnectionStringAsync()
    {
        if (!started)
        {
            container ??= new PostgreSqlBuilder("pgvector/pgvector:pg16")
                .WithDatabase("incidentcompass_tests")
                .WithUsername("incidentcompass")
                .WithPassword("incidentcompass_dev_password")
                .Build();
            await container.StartAsync();
            started = true;
        }

        return container!.GetConnectionString();
    }
}

public sealed class DockerAvailableFactAttribute : FactAttribute
{
    public DockerAvailableFactAttribute(
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IsDockerEndpointLikelyAvailable() && !IsDockerRequiredEnvironment())
        {
            Skip = "Docker is not available for PostgreSQL integration tests.";
        }
    }

    private static bool IsDockerEndpointLikelyAvailable()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return CanConnectToNamedPipe("docker_engine") ||
                   CanConnectToNamedPipe("dockerDesktopLinuxEngine");
        }

        return File.Exists("/var/run/docker.sock") ||
               File.Exists(Path.Combine(
                   Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                   ".docker/run/docker.sock"));
    }

    private static bool CanConnectToNamedPipe(string pipeName)
    {
        try
        {
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                ".",
                pipeName,
                System.IO.Pipes.PipeDirection.InOut);
            pipe.Connect(100);
            return pipe.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDockerRequiredEnvironment()
    {
        return IsTruthy(Environment.GetEnvironmentVariable("CI")) ||
               IsTruthy(Environment.GetEnvironmentVariable("INCIDENTCOMPASS_REQUIRE_DOCKER_TESTS"));
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
               (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}