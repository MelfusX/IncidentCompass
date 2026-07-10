using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.UnitTests;

public sealed class PostgresOperationTests
{
    [Fact]
    public async Task ExecuteAsync_NpgsqlFailure_NormalizesToApplicationException()
    {
        var providerException = new NpgsqlException("database unavailable");

        var exception = await Assert.ThrowsAsync<PersistenceException>(
            () => PostgresOperation.ExecuteAsync(
                "read fault",
                () => Task.FromException(providerException)));

        Assert.Equal("read fault", exception.Operation);
        Assert.Same(providerException, exception.InnerException);
        Assert.DoesNotContain("database unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_NormalizesToApplicationException()
    {
        var timeoutException = new TimeoutException("command timeout");

        var exception = await Assert.ThrowsAsync<PersistenceException>(
            () => PostgresOperation.ExecuteAsync<int>(
                "claim job",
                () => Task.FromException<int>(timeoutException)));

        Assert.Equal("claim job", exception.Operation);
        Assert.Same(timeoutException, exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_NonInfrastructureFailure_PreservesOriginalException()
    {
        var applicationFailure = new InvalidOperationException("invariant failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PostgresOperation.ExecuteAsync(
                "publish report",
                () => Task.FromException(applicationFailure)));

        Assert.Same(applicationFailure, exception);
    }
}
