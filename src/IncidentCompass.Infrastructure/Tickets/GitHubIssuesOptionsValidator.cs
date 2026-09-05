using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Tickets;

internal sealed partial class GitHubIssuesOptionsValidator : IValidateOptions<GitHubIssuesOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubIssuesOptions options)
    {
        if (options.TimeoutSeconds is < 1 or > 60)
        {
            return ValidateOptionsResult.Fail("GitHub Issues TimeoutSeconds must be between 1 and 60.");
        }

        var hasOwner = !string.IsNullOrWhiteSpace(options.Owner);
        var hasRepository = !string.IsNullOrWhiteSpace(options.Repository);
        if (hasOwner != hasRepository)
        {
            return ValidateOptionsResult.Fail("GitHub Issues Owner and Repository must be configured together.");
        }

        if (hasOwner && !OwnerRegex().IsMatch(options.Owner!))
        {
            return ValidateOptionsResult.Fail("GitHub Issues Owner is invalid.");
        }

        if (hasRepository &&
            (!RepositoryRegex().IsMatch(options.Repository!) || options.Repository is "." or ".."))
        {
            return ValidateOptionsResult.Fail("GitHub Issues Repository is invalid.");
        }

        return ValidateOptionsResult.Success;
    }

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerRegex();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryRegex();
}
