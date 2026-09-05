using IncidentCompass.Worker;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class PostReportActionEvaluationOptionsTests
{
    [Fact]
    public void DefaultsMatchTheBoundedEvaluationContract()
    {
        var options = new PostReportActionEvaluationOptions();

        Assert.Equal(30, options.LeaseSeconds);
        Assert.Equal(3, options.MaximumAttempts);
        Assert.Equal(5, options.FirstRetryDelaySeconds);
        Assert.Equal(10, options.SecondRetryDelaySeconds);
        Assert.Equal(8, options.ScanBatchSize);
        Assert.Equal(5, options.PollIntervalSeconds);
        Assert.Equal(4, options.MaxConcurrency);
        Assert.True(CreateValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0, 3, 5, 10, 8, 5, 4)]
    [InlineData(30, 0, 5, 10, 8, 5, 4)]
    [InlineData(30, 3, 0, 10, 8, 5, 4)]
    [InlineData(30, 3, 11, 10, 8, 5, 4)]
    [InlineData(30, 3, 5, 10, 0, 5, 4)]
    [InlineData(30, 3, 5, 10, 8, 0, 4)]
    [InlineData(30, 3, 5, 10, 8, 5, 0)]
    public void InvalidBoundsFailStartup(
        int lease,
        int attempts,
        int firstRetry,
        int secondRetry,
        int batch,
        int poll,
        int concurrency)
    {
        var result = CreateValidator().Validate(null, new PostReportActionEvaluationOptions
        {
            LeaseSeconds = lease,
            MaximumAttempts = attempts,
            FirstRetryDelaySeconds = firstRetry,
            SecondRetryDelaySeconds = secondRetry,
            ScanBatchSize = batch,
            PollIntervalSeconds = poll,
            MaxConcurrency = concurrency
        });

        Assert.False(result.Succeeded);
    }

    private static IValidateOptions<PostReportActionEvaluationOptions> CreateValidator()
    {
        var type = typeof(PostReportActionEvaluationOptions).Assembly.GetType(
            "IncidentCompass.Worker.PostReportActionEvaluationOptionsValidator")
            ?? throw new InvalidOperationException("Post-report evaluation options validator was not found.");
        return (IValidateOptions<PostReportActionEvaluationOptions>)Activator.CreateInstance(type, nonPublic: true)!;
    }
}
