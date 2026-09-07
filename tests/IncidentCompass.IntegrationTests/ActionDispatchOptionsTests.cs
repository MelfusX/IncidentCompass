using IncidentCompass.Worker;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class ActionDispatchOptionsTests
{
    [Fact]
    public void DefaultsAreWithinTheDispatchContract()
    {
        var options = new ActionDispatchOptions();

        Assert.InRange(options.BatchSize, 1, 64);
        Assert.InRange(options.PollIntervalSeconds, 1, 60);
        Assert.InRange(options.AdapterTimeoutSeconds, 1, 300);
        Assert.True(CreateValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0, 5, 30)]
    [InlineData(65, 5, 30)]
    [InlineData(8, 0, 30)]
    [InlineData(8, 61, 30)]
    [InlineData(8, 5, 0)]
    [InlineData(8, 5, 301)]
    public void ContractBoundsFailStartup(int batch, int poll, int timeout)
    {
        var result = CreateValidator().Validate(null, new ActionDispatchOptions
        {
            BatchSize = batch,
            PollIntervalSeconds = poll,
            AdapterTimeoutSeconds = timeout
        });

        Assert.False(result.Succeeded);
    }

    private static IValidateOptions<ActionDispatchOptions> CreateValidator()
    {
        var type = typeof(ActionDispatchOptions).Assembly.GetType(
            "IncidentCompass.Worker.ActionDispatchOptionsValidator")
            ?? throw new InvalidOperationException("ActionDispatchOptionsValidator type was not found.");
        return (IValidateOptions<ActionDispatchOptions>)Activator.CreateInstance(type, nonPublic: true)!;
    }
}
