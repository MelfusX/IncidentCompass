using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Normalization;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.Intake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace IncidentCompass.UnitTests;

[Collection(TriageConfigurationValidationParityTests.ConsoleCollectionName)]
public sealed class TriageConfigurationValidationParityTests
{
    public const string ConsoleCollectionName = "Triage configuration console output";
    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    public static TheoryData<string, bool, bool, string?> FixtureCases => new()
    {
        { "valid", true, true, null },
        { "provider", false, false, "Providers.local-oai.Kind" },
        { "route", false, false, "Routes.analysis-chat.Model" },
        { "role", false, false, "Roles.analysis.OutputSchema" },
        { "tool", false, false, "Tools.memory_search.Kind" },
        { "rule", false, false, "Rules.Type" },
        { "budget", false, false, "Orchestrator.Budget.MaxTokens" },
        { "ingestion", false, false, "Ingestion.AllowedSources" },
        { "grouping", false, false, "FaultGrouping.LookbackMinutes" },
        { "redaction", false, false, "Redaction.Patterns[0].Name" },
        { "dangling-role-route", true, false, "Roles.analysis.RouteId" }
    };

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public async Task Schema_Command_AndStartup_UseTheExpectedValidationBoundary(
        string fixtureName,
        bool schemaIsValid,
        bool runtimeIsValid,
        string? expectedSection)
    {
        using var fixture = TemporaryConfigFixture.Create(fixtureName);
        var configNode = fixture.LoadConfig();
        ApplyFixture(fixtureName, configNode);
        fixture.SaveConfig(configNode);

        var schemaResult = TriageConfigSchemaTests.EvaluateFixture(configNode);
        Assert.Equal(schemaIsValid, schemaResult.IsValid);

        var snapshots = new RecordingSnapshotStore();
        var hostProbe = new HostedServiceProbe();
        await using var services = CreateServices(fixture.RootPath, snapshots, hostProbe);
        var repository = services.GetRequiredService<FileTriageConfigurationRepository>();
        var warmup = new TriageConfigurationWarmupHostedService(repository);
        string? startupMessage = null;

        if (runtimeIsValid)
        {
            await warmup.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, snapshots.PersistCallCount);
        }
        else
        {
            var startupException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => warmup.StartAsync(TestContext.Current.CancellationToken));
            startupMessage = startupException.Message;
            Assert.Contains(expectedSection!, startupMessage, StringComparison.Ordinal);
            Assert.Equal(0, snapshots.PersistCallCount);
        }

        var command = await RunCommandAsync(services);

        Assert.Equal(runtimeIsValid ? 0 : 1, command.ExitCode);
        Assert.False(hostProbe.StartCalled);
        if (runtimeIsValid)
        {
            Assert.Contains(fixture.ConfigPath, command.StandardOutput, StringComparison.Ordinal);
            Assert.Empty(command.StandardError);
            Assert.Equal(1, snapshots.PersistCallCount);
        }
        else
        {
            Assert.Contains(fixture.ConfigPath, command.StandardError, StringComparison.Ordinal);
            Assert.Contains(expectedSection!, command.StandardError, StringComparison.Ordinal);
            Assert.Contains(startupMessage!, command.StandardError, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ConfigValidate_DoesNotStartAHostOrPersistASnapshot()
    {
        using var fixture = TemporaryConfigFixture.Create("valid");
        var snapshots = new RecordingSnapshotStore();
        var hostProbe = new HostedServiceProbe();
        await using var services = CreateServices(fixture.RootPath, snapshots, hostProbe);

        var command = await RunCommandAsync(services);

        Assert.Equal(0, command.ExitCode);
        Assert.Contains(fixture.ConfigPath, command.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(command.StandardError);
        Assert.False(hostProbe.StartCalled);
        Assert.Equal(0, snapshots.PersistCallCount);
    }

    private static void ApplyFixture(string fixtureName, JsonObject root)
    {
        switch (fixtureName)
        {
            case "valid":
                return;
            case "provider":
                root["Providers"]!["local-oai"]!["Kind"] = "Unsupported";
                return;
            case "route":
                root["Routes"]!["analysis-chat"]!["Model"] = string.Empty;
                return;
            case "role":
                root["Roles"]!["analysis"]!["OutputSchema"] = string.Empty;
                return;
            case "tool":
                root["Tools"]!["memory_search"]!["Kind"] = "external";
                return;
            case "rule":
                root["Rules"]![0]!["Type"] = "unknown";
                return;
            case "budget":
                root["Orchestrator"]!["Budget"]!["MaxTokens"] = 0;
                return;
            case "ingestion":
                root["Ingestion"]!["AllowedSources"] = new JsonArray("webhook");
                return;
            case "grouping":
                root["FaultGrouping"]!["LookbackMinutes"] = 0;
                return;
            case "redaction":
                root["Redaction"]!["Patterns"] = new JsonArray(new JsonObject
                {
                    ["Name"] = string.Empty,
                    ["Pattern"] = "secret"
                });
                return;
            case "dangling-role-route":
                root["Roles"]!["analysis"]!["RouteId"] = "missing-route";
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(fixtureName), fixtureName, "Unknown fixture.");
        }
    }

    private static ServiceProvider CreateServices(
        string contentRootPath,
        RecordingSnapshotStore snapshots,
        HostedServiceProbe hostProbe)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRootPath));
        services.AddSingleton<IOptions<TriageConfigSourceOptions>>(Options.Create(new TriageConfigSourceOptions
        {
            Kind = "File",
            Path = Path.Combine("config", "incidentcompass.config.json")
        }));
        services.AddSingleton(new SignalNormalizerRegistry([
            new TesterSignalNormalizer(),
            new OtelShapedSignalNormalizer(),
            new UserReportSignalNormalizer()
        ]));
        services.AddSingleton<TriageConfigurationLoadValidator>();
        services.AddSingleton<TriageConfigurationMaterializer>();
        services.AddSingleton<ITriageConfigurationSnapshotStore>(snapshots);
        services.AddSingleton<FileTriageConfigurationRepository>();
        services.AddSingleton<IHostedService>(hostProbe);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<CommandResult> RunCommandAsync(IServiceProvider services)
    {
        await ConsoleLock.WaitAsync(TestContext.Current.CancellationToken);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = await TriageConfigurationValidationCommand.RunIfRequestedAsync(
                ["config", "validate"],
                services,
                TestContext.Current.CancellationToken);
            return new CommandResult(exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            ConsoleLock.Release();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record CommandResult(int? ExitCode, string StandardOutput, string StandardError);

    private sealed class RecordingSnapshotStore : ITriageConfigurationSnapshotStore
    {
        public int PersistCallCount { get; private set; }

        public Task PersistAsync(
            string configHash,
            JsonNode configNode,
            JsonObject instructionsNode,
            CancellationToken cancellationToken)
        {
            PersistCallCount++;
            return Task.CompletedTask;
        }

        public Task<TriageConfigurationSnapshotDocument?> GetAsync(
            string configHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<TriageConfigurationSnapshotDocument?>(null);
    }

    private sealed class HostedServiceProbe : IHostedService
    {
        public bool StartCalled { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalled = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "IncidentCompass.UnitTests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TemporaryConfigFixture : IDisposable
    {
        private TemporaryConfigFixture(string rootPath)
        {
            RootPath = rootPath;
            ConfigPath = Path.Combine(RootPath, "config", "incidentcompass.config.json");
        }

        public string RootPath { get; }
        public string ConfigPath { get; }

        public static TemporaryConfigFixture Create(string fixtureName)
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "IncidentCompass", "config-validation", fixtureName, Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(FindRepositoryRoot(), "config");
            foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                var destinationPath = Path.Combine(rootPath, "config", relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourceFile, destinationPath);
            }

            return new TemporaryConfigFixture(rootPath);
        }

        public JsonObject LoadConfig() => JsonNode.Parse(File.ReadAllText(ConfigPath))!.AsObject();

        public void SaveConfig(JsonObject config) => File.WriteAllText(ConfigPath, config.ToJsonString());

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

[CollectionDefinition(TriageConfigurationValidationParityTests.ConsoleCollectionName, DisableParallelization = true)]
public sealed class TriageConfigurationConsoleCollection;
