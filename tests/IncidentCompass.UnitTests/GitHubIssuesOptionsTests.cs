using IncidentCompass.Infrastructure.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class GitHubIssuesOptionsTests
{
    [Theory]
    [InlineData("-owner", "repo")]
    [InlineData("owner-", "repo")]
    [InlineData("owner", "repo/name")]
    [InlineData("owner", "..")]
    public void Validator_RejectsInvalidRepositoryIdentity(string owner, string repository)
    {
        var result = new GitHubIssuesOptionsValidator().Validate(null, new GitHubIssuesOptions
        {
            Owner = owner,
            Repository = repository
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Options_AreDisabledWithoutSecretAndHandlerRejectsRedirects()
    {
        var options = new GitHubIssuesOptions { Owner = "owner", Repository = "repo" };
        using var handler = Assert.IsType<SocketsHttpHandler>(GitHubIssuesHttpMessageHandlerFactory.Create());

        Assert.False(options.IsConfigured);
        Assert.Equal("owner/repo", options.ConfiguredRepository);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal("https", GitHubIssuesTicketSearch.Authority.Scheme);
        Assert.Equal(443, GitHubIssuesTicketSearch.Authority.Port);
        Assert.Equal("api.github.com", GitHubIssuesTicketSearch.Authority.Host);
    }

    [Fact]
    public void WorkerAppSettings_DoNotPersistGitHubToken()
    {
        var root = FindRepositoryRoot();
        foreach (var name in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var content = File.ReadAllText(Path.Combine(root, "src", "IncidentCompass.Worker", name));
            Assert.DoesNotContain("\"Token\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Authorization", content, StringComparison.OrdinalIgnoreCase);
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
}
