namespace IncidentCompass.Tester;

internal static class DemoDeadlineRunner
{
    public static async Task<int> RunTotalAsync(
        TimeSpan timeout,
        Func<CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await action(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            Console.Error.WriteLine("Tester timed out after total deadline " + Format(timeout) + ".");
            return 1;
        }
        catch (TimeoutException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    public static async Task<T> RunScenarioAsync<T>(
        DemoScenario scenario,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await action(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Scenario " + scenario.Id + " " + scenario.Name + " exceeded per-scenario deadline " + Format(timeout) + ".");
        }
    }

    private static string Format(TimeSpan timeout)
    {
        return timeout.TotalMinutes >= 1
            ? timeout.TotalMinutes.ToString("0.#") + " minutes"
            : timeout.TotalSeconds.ToString("0.#") + " seconds";
    }
}
