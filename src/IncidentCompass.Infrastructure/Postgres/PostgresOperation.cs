using IncidentCompass.Application.Core.Exceptions;
using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresOperation
{
    public static async Task ExecuteAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw new PersistenceException(operation, exception);
        }
    }

    public static async Task<TResult> ExecuteAsync<TResult>(
        string operation,
        Func<Task<TResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (IsInfrastructureFailure(exception))
        {
            throw new PersistenceException(operation, exception);
        }
    }

    private static bool IsInfrastructureFailure(Exception exception)
    {
        return exception is NpgsqlException or TimeoutException;
    }
}
