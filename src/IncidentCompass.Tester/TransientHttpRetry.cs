using System.Net;

namespace IncidentCompass.Tester;

internal sealed class TransientHttpRetry(TimeSpan delay)
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (HttpRequestException exception) when (IsTransient(exception.StatusCode))
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode? statusCode)
    {
        return statusCode is null ||
               statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
               (int)statusCode >= 500;
    }
}