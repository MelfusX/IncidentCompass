using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

internal sealed class PostReportActionEvaluationOptionsValidator
    : IValidateOptions<PostReportActionEvaluationOptions>
{
    public ValidateOptionsResult Validate(string? name, PostReportActionEvaluationOptions options)
    {
        var failures = new List<string>();
        AddRange(failures, options.LeaseSeconds, 1, 300, nameof(options.LeaseSeconds));
        AddRange(failures, options.MaximumAttempts, 1, 10, nameof(options.MaximumAttempts));
        AddRange(failures, options.FirstRetryDelaySeconds, 1, 300, nameof(options.FirstRetryDelaySeconds));
        AddRange(failures, options.SecondRetryDelaySeconds, 1, 600, nameof(options.SecondRetryDelaySeconds));
        AddRange(failures, options.ScanBatchSize, 1, 64, nameof(options.ScanBatchSize));
        AddRange(failures, options.PollIntervalSeconds, 1, 60, nameof(options.PollIntervalSeconds));
        AddRange(failures, options.MaxConcurrency, 1, 64, nameof(options.MaxConcurrency));
        if (options.SecondRetryDelaySeconds < options.FirstRetryDelaySeconds)
        {
            failures.Add(
                "IncidentCompass:PostReportActions:SecondRetryDelaySeconds must not be less than FirstRetryDelaySeconds.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRange(
        List<string> failures,
        int value,
        int minimum,
        int maximum,
        string property)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"IncidentCompass:PostReportActions:{property} must be between {minimum} and {maximum}.");
        }
    }
}
