using IncidentCompass.TestSupport;

namespace IncidentCompass.IntegrationTests;

public sealed class ConfigurationSecretTests
{
    [Theory]
    [InlineData("src/IncidentCompass.Api/appsettings.json")]
    [InlineData("src/IncidentCompass.Worker/appsettings.json")]
    public async Task RuntimeAppSettings_DoNotContainPostgresPasswords(string relativePath)
    {
        var content = await File.ReadAllTextAsync(
            Path.Combine(RepositoryRootLocator.Find(), relativePath));

        Assert.DoesNotContain("incidentcompass_dev_password", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("config/incidentcompass.config.json")]
    [InlineData("tests/IncidentCompass.IntegrationTests/Fixtures/test-triage-config/incidentcompass.config.json")]
    [InlineData("tests/IncidentCompass.IntegrationTests/Fixtures/retriage-triage-config/incidentcompass.config.json")]
    public async Task TriageConfigurationContainsNoTelegramHostAuthorityOrCredential(string relativePath)
    {
        var content = await File.ReadAllTextAsync(Path.Combine(RepositoryRootLocator.Find(), relativePath));

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
        var content = await File.ReadAllTextAsync(Path.Combine(RepositoryRootLocator.Find(), relativePath));

        Assert.DoesNotContain("GitHub", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.github.com", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner/repo", content, StringComparison.OrdinalIgnoreCase);
    }
}
