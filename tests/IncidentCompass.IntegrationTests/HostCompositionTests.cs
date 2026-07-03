using System.Runtime.CompilerServices;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Security;
using IncidentCompass.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using IncidentCompass.Worker;
using WorkerService = IncidentCompass.Worker.Worker;

namespace IncidentCompass.IntegrationTests;

public sealed class HostCompositionTests
{
    public static IEnumerable<object[]> InvalidApplicationConfigurations =>
    [
        [new Dictionary<string, string?> { ["IncidentCompass:Application:ApiVersion"] = " " }],
        [new Dictionary<string, string?> { ["IncidentCompass:Application:RunnerVersion"] = "" }]
    ];

    public static IEnumerable<object[]> InvalidModelGatewayConfigurations =>
    [
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:DefaultModel"] = "" }],
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:StrongModel"] = " " }],
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:DefaultTemperature"] = "1.5" }],
        [
            new Dictionary<string, string?>
            {
                ["IncidentCompass:ModelGateway:MinTemperature"] = "0.8",
                ["IncidentCompass:ModelGateway:MaxTemperature"] = "0.7"
            }
        ],
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:DefaultMaxOutputTokens"] = "0" }],
        [
            new Dictionary<string, string?>
            {
                ["IncidentCompass:ModelGateway:DefaultMaxOutputTokens"] = "4096",
                ["IncidentCompass:ModelGateway:MaxOutputTokensLimit"] = "2048"
            }
        ],
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:MaxInputMessageCharacters"] = "0" }],
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:MaxCorrelationIdLength"] = "129" }],
        [new Dictionary<string, string?> { ["IncidentCompass:ModelGateway:AllowedModels:0"] = " " }]
    ];

    public static IEnumerable<object[]> AcceptedProviderSpellings =>
    [
        ["Mock"],
        ["OpenAiCompatible"],
        ["OPENAI_COMPATIBLE"],
        ["OPENAI-COMPATIBLE"]
    ];

    [Fact]
    public void WorkerHostServices_CanBuildWithScopeValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IncidentCompass:Application:ApiVersion"] = "v1",
                ["IncidentCompass:Postgres:ConnectionStringName"] = "IncidentCompass"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestApplication(configuration);
        services.AddInfrastructure(configuration);
        services.AddWorker(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        // Worker + Infrastructure warmups for triage config and optional memory seeding.
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        Assert.Equal(3, hostedServices.Length);
        Assert.Contains(hostedServices, service => service is WorkerService);
        Assert.Contains(hostedServices, service =>
            service.GetType().FullName == "IncidentCompass.Infrastructure.Intake.TriageConfigurationWarmupHostedService");
        Assert.Contains(hostedServices, service =>
            service.GetType().FullName == "IncidentCompass.Infrastructure.Memory.MemorySeedHostedService");
        using var scope = provider.CreateScope();
        var backgroundContext = scope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();
        Assert.Same(backgroundContext, userContext);
        Assert.True(userContext.IsAuthenticated);
        Assert.Equal("system", userContext.UserId);
        Assert.Null(userContext.TenantId);
        Assert.Contains("system", userContext.Roles);
    }

    [Fact]
    public async Task HostServices_RejectUnsupportedModelGatewayProviderOnStart()
    {
        using var host = new HostBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IncidentCompass:ModelGateway:Provider"] = "TypoProvider"
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddTestApplication(context.Configuration);
                services.AddInfrastructure(context.Configuration);
            })
            .Build();

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.NotNull(exception);
        Assert.Contains(
            GetOptionsValidationFailures(exception),
            failure => failure.Contains("unsupported", StringComparison.OrdinalIgnoreCase) &&
                       failure.Contains("TypoProvider", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AcceptedProviderSpellings))]
    public async Task HostServices_AcceptsDocumentedProviderSpellings(string provider)
    {
        using var host = CreateHostWithConfiguration(new Dictionary<string, string?>
        {
            ["IncidentCompass:ModelGateway:Provider"] = provider,
            ["IncidentCompass:ModelGateway:OpenAiCompatible:ApiKey"] = "test-api-key",
            ["IncidentCompass:Embeddings:Provider"] = provider,
            ["IncidentCompass:Embeddings:OpenAiCompatible:ApiKey"] = "test-api-key"
        });

        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAiModelClient>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEmbeddingClient>());
    }

    [Theory]
    [MemberData(nameof(InvalidApplicationConfigurations))]
    public async Task HostServices_RejectInvalidApplicationOptionsOnStart(
        IReadOnlyDictionary<string, string?> invalidConfiguration)
    {
        using var host = CreateHostWithConfiguration(invalidConfiguration);

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        // [OptionsValidator] (source-generated) emits one failure per failing property, formatted
        // by the attribute's ErrorMessage. The invalidated field name appears in the failure
        // text, which is what we anchor the assertion on now.
        var expectedFieldName = invalidConfiguration.Keys.First().Split(':')[^1];
        Assert.NotNull(exception);
        Assert.Contains(
            GetOptionsValidationFailures(exception),
            failure => failure.Contains(expectedFieldName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostServices_RejectInvalidEmbeddingOptionsOnStart()
    {
        using var host = CreateHostWithConfiguration(new Dictionary<string, string?>
        {
            ["IncidentCompass:Embeddings:DefaultModel"] = ""
        });

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        // [OptionsValidator] (source-generated) emits one failure per failing property; the
        // invalidated field name appears in the failure text.
        Assert.NotNull(exception);
        Assert.Contains(
            GetOptionsValidationFailures(exception),
            failure => failure.Contains("DefaultModel", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(InvalidModelGatewayConfigurations))]
    public async Task HostServices_RejectInvalidModelGatewayOptionsOnStart(
        IReadOnlyDictionary<string, string?> values)
    {
        using var host = CreateHostWithConfiguration(values);

        var exception = await Record.ExceptionAsync(() => host.StartAsync());

        Assert.NotNull(exception);
        Assert.Contains(
            GetOptionsValidationFailures(exception),
            failure => failure.Contains("Model gateway configuration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MockEmbeddingClient_UsesConfiguredDimensions()
    {
        using var host = CreateHostWithConfiguration(new Dictionary<string, string?>
        {
            ["IncidentCompass:Embeddings:MockDimensions"] = "1024"
        });
        await host.StartAsync();

        var embeddingClient = host.Services.GetRequiredService<IEmbeddingClient>();
        var response = await embeddingClient.CreateEmbeddingAsync(
            new EmbeddingRequest("dimension test", "mock-embedding", CorrelationId: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(1024, response.Vector.Count);
    }

    private static IHost CreateHostWithConfiguration(
        IReadOnlyDictionary<string, string?> values)
    {
        // These hosts exercise ModelGateway/Embeddings option validation only, but AddInfrastructure
        // now also registers Phase 1 intake infrastructure, whose warmup hosted service needs a
        // real triage config file to resolve at StartAsync -- point it at the repo's checked-in
        // config so these unrelated tests do not need to know about intake at all.
        var configurationOverrides = new Dictionary<string, string?>(values)
        {
            ["IncidentCompass:ConfigSource:Path"] = Path.Combine(FindRepositoryRoot(), "config", "incidentcompass.config.json")
        };

        return new HostBuilder()
            .ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(configurationOverrides);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddTestApplication(context.Configuration);
                services.AddInfrastructure(context.Configuration);
            })
            .Build();
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new FileInfo(sourceFilePath).Directory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static IEnumerable<string> GetOptionsValidationFailures(Exception exception)
    {
        if (exception is OptionsValidationException optionsValidationException)
        {
            return optionsValidationException.Failures;
        }

        if (exception is AggregateException aggregateException)
        {
            return aggregateException
                .Flatten()
                .InnerExceptions
                .OfType<OptionsValidationException>()
                .SelectMany(static optionsValidationException => optionsValidationException.Failures);
        }

        return [];
    }
}
