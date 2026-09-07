using IncidentCompass.Application;
using IncidentCompass.Application.Core.Composition;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.UnitTests;

public sealed class ApplicationWiringValidatorTests
{
    [Fact]
    public void ValidateApplicationWiring_RejectsCompositionWithoutInfrastructure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(BuildConfiguration());

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.ValidateApplicationWiring());

        Assert.Contains(nameof(IClaimedTriageJobProcessor), exception.Message, StringComparison.Ordinal);
        Assert.Contains("deferred placeholder", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddInfrastructure", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateApplicationWiring_RejectsCompositionWithoutRecurrenceScheduler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(BuildConfiguration());
        services.AddScoped<IClaimedTriageJobProcessor, StubClaimedTriageJobProcessor>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.ValidateApplicationWiring());

        Assert.Contains(
            nameof(IRecurrenceEscalationReTriageScheduler), exception.Message, StringComparison.Ordinal);
        Assert.Contains("AddInfrastructure", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateApplicationWiring_AcceptsApplicationPlusInfrastructure()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);

        Assert.Same(services, services.ValidateApplicationWiring());
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IncidentCompass:Application:ApiVersion"] = "v1"
            })
            .Build();

    private sealed class StubClaimedTriageJobProcessor : IClaimedTriageJobProcessor
    {
        public Task ProcessAsync(
            Domain.Incidents.TriageJob job,
            Application.Intake.Configuration.TriageConfiguration configuration,
            string workerId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
