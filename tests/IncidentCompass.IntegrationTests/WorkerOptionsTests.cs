using System.Text.Json.Nodes;
using IncidentCompass.Worker;
using IncidentCompass.Infrastructure.Notifications.Telegram;
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
    public void DevelopmentLease_ExceedsShippedInvestigationWallClockBudget()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workerSettings = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "src", "IncidentCompass.Worker", "appsettings.Development.json")))!;
        var triageSettings = JsonNode.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "config", "incidentcompass.config.json")))!;

        var leaseSeconds = workerSettings["IncidentCompass"]!["Worker"]!["LeaseSeconds"]!.GetValue<int>();
        var wallClockSeconds = triageSettings["Orchestrator"]!["Budget"]!["MaxWallClockSeconds"]!.GetValue<int>();

        Assert.True(
            leaseSeconds > wallClockSeconds,
            $"Development lease ({leaseSeconds}s) must exceed the shipped investigation budget ({wallClockSeconds}s).");
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

    [Fact]
    public void ShippedTelegramBindingIsDisabledAndContainsNoCredential()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "IncidentCompass.Worker", "appsettings.json")))!;
        var telegram = settings["IncidentCompass"]!["Telegram"]!;

        Assert.False(telegram["Enabled"]!.GetValue<bool>());
        Assert.Equal(string.Empty, telegram["RouteId"]!.GetValue<string>());
        Assert.Equal(string.Empty, telegram["ChatId"]!.GetValue<string>());
        Assert.Equal(string.Empty, telegram["BotToken"]!.GetValue<string>());
        Assert.True(new TelegramOptionsValidator().Validate(null, new TelegramOptions()).Succeeded);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the IncidentCompass repository root.");
    }

    private static IValidateOptions<WorkerOptions> CreateValidator()
    {
        var type = typeof(WorkerOptions).Assembly.GetType("IncidentCompass.Worker.WorkerOptionsValidator")
            ?? throw new InvalidOperationException("WorkerOptionsValidator type was not found.");
        return (IValidateOptions<WorkerOptions>)Activator.CreateInstance(type, nonPublic: true)!;
    }
}
