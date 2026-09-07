namespace IncidentCompass.Infrastructure.Tickets;

public sealed class GitHubIssuesOptions
{
    public const string SectionName = "IncidentCompass:Tickets:GitHub";

    public string? Owner { get; init; }

    public string? Repository { get; init; }

    public string? Token { get; init; }

    public int TimeoutSeconds { get; init; } = 10;

    public string? ConfiguredRepository =>
        string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repository)
            ? null
            : $"{Owner}/{Repository}";

    public bool IsConfigured =>
        ConfiguredRepository is not null && !string.IsNullOrWhiteSpace(Token);
}
