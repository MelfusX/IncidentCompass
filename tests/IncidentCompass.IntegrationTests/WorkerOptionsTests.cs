using IncidentCompass.Worker;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class WorkerOptionsTests
{
    [Fact]
    public void Defaults_UseLongerLease()
    {
        var options = new WorkerOptions();

        Assert.Equal(900, options.LeaseSeconds);
    }

    [Fact]
    public void Validator_RejectsNonPositivePollAndLeaseSeconds()
    {
        var validator = CreateValidator();
        var options = new WorkerOptions
        {
            MaxConcurrentJobs = 1,
            PollIntervalSeconds = 0,
            LeaseSeconds = 0,
            MaxAttempts = 1,
            RetryDelaySeconds = 0
        };

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("PollIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("LeaseSeconds", StringComparison.Ordinal));
    }

    private static IValidateOptions<WorkerOptions> CreateValidator()
    {
        var type = typeof(WorkerOptions).Assembly.GetType("IncidentCompass.Worker.WorkerOptionsValidator")
            ?? throw new InvalidOperationException("WorkerOptionsValidator type was not found.");
        return (IValidateOptions<WorkerOptions>)Activator.CreateInstance(type, nonPublic: true)!;
    }
}
