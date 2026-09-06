using IncidentCompass.Infrastructure;
using IncidentCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IncidentCompass.UnitTests;

public sealed class PostgresConnectionOptionsTests
{
    private const string DeployedConnectionString =
        "Host=postgres;Port=5432;Database=incidentcompass;Username=incidentcompass;Password=dev";

    [Fact]
    public void AddInfrastructure_BindsTheDeployedConnectionStringsKey()
    {
        // ConnectionStrings__IncidentCompass (.env.example, docker-compose.yml) reaches
        // configuration as ConnectionStrings:IncidentCompass.
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["ConnectionStrings:IncidentCompass"] = DeployedConnectionString
        });

        var options = provider.GetRequiredService<IOptions<PostgresConnectionOptions>>().Value;

        Assert.Equal("IncidentCompass", options.ConnectionStringName);
        Assert.Equal(DeployedConnectionString, options.ConnectionString);
    }

    [Fact]
    public void AddInfrastructure_HonoursAnOverriddenConnectionStringName()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["IncidentCompass:Postgres:ConnectionStringName"] = "MigrationTests",
            ["ConnectionStrings:MigrationTests"] = DeployedConnectionString,
            ["ConnectionStrings:IncidentCompass"] = "Host=wrong"
        });

        var options = provider.GetRequiredService<IOptions<PostgresConnectionOptions>>().Value;

        Assert.Equal("MigrationTests", options.ConnectionStringName);
        Assert.Equal(DeployedConnectionString, options.ConnectionString);
    }

    [Fact]
    public void AddInfrastructure_LeavesAMissingConnectionStringUnbound()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var options = provider.GetRequiredService<IOptions<PostgresConnectionOptions>>().Value;

        Assert.Equal("IncidentCompass", options.ConnectionStringName);
        Assert.Null(options.ConnectionString);
    }

    [Fact]
    public void AddInfrastructure_DoesNotRegisterConfigurationInTheContainer()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestApplication(configuration);
        services.AddInfrastructure(configuration);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IConfiguration));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = BuildConfiguration(values);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestApplication(configuration);
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        var configurationValues = new Dictionary<string, string?>(values)
        {
            ["IncidentCompass:Application:ApiVersion"] = "v1"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
    }
}
