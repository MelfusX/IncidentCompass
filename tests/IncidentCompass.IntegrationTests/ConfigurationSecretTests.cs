using System.Runtime.CompilerServices;

namespace IncidentCompass.IntegrationTests;

public sealed class ConfigurationSecretTests
{
    [Theory]
    [InlineData("src/IncidentCompass.Api/appsettings.json")]
    [InlineData("src/IncidentCompass.Worker/appsettings.json")]
    public async Task RuntimeAppSettings_DoNotContainPostgresPasswords(string relativePath)
    {
        var content = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), relativePath));

        Assert.DoesNotContain("incidentcompass_dev_password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("config/incidentcompass.config.json")]
    [InlineData("tests/IncidentCompass.IntegrationTests/Fixtures/test-triage-config/incidentcompass.config.json")]
    [InlineData("tests/IncidentCompass.IntegrationTests/Fixtures/retriage-triage-config/incidentcompass.config.json")]
    public async Task TriageConfigurationContainsNoTelegramHostAuthorityOrCredential(string relativePath)
    {
        var content = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), relativePath));

        Assert.DoesNotContain("BotToken", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ChatId", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.telegram.org", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("config/incidentcompass.config.json")]
    [InlineData("tests/IncidentCompass.IntegrationTests/Fixtures/test-triage-config/incidentcompass.config.json")]
    [InlineData("tests/IncidentCompass.IntegrationTests/Fixtures/retriage-triage-config/incidentcompass.config.json")]
    public async Task TriageConfigurationContainsNoGitHubHostAuthorityOrCredential(string relativePath)
    {
        var content = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), relativePath));

        Assert.DoesNotContain("GitHub", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.github.com", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner/repo", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot(
        [CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { sourceFilePath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = File.Exists(startPath)
                ? new FileInfo(startPath).Directory
                : new DirectoryInfo(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "IncidentCompass.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
