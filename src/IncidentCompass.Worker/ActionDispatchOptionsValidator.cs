using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

internal sealed class ActionDispatchOptionsValidator : IValidateOptions<ActionDispatchOptions>
{
    public ValidateOptionsResult Validate(string? name, ActionDispatchOptions options)
    {
        var failures = new List<string>();
        AddRangeFailure(failures, options.BatchSize, 1, 64, nameof(options.BatchSize));
        AddRangeFailure(failures, options.PollIntervalSeconds, 1, 60, nameof(options.PollIntervalSeconds));
        AddRangeFailure(failures, options.AdapterTimeoutSeconds, 1, 300, nameof(options.AdapterTimeoutSeconds));
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRangeFailure(
        List<string> failures,
        int value,
        int minimum,
        int maximum,
        string name)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"IncidentCompass:ActionDispatch:{name} must be between {minimum} and {maximum}.");
        }
    }
}
