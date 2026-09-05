using Microsoft.Extensions.Options;

namespace IncidentCompass.Api.Configuration;

internal sealed class ApiKeyAuthOptionsValidator : IValidateOptions<ApiKeyAuthOptions>
{
    private const int MaxCredentials = 256;

    public ValidateOptionsResult Validate(string? name, ApiKeyAuthOptions options)
    {
        var failures = new List<string>();
        ValidateStaticFields(options, failures);
        var credentials = options.Credentials ?? [];

        if (credentials.Length > MaxCredentials)
        {
            failures.Add($"Credentials must contain at most {MaxCredentials} entries.");
        }

        if (options.Enabled && credentials.Length == 0)
        {
            failures.Add("Credentials must contain at least one entry when API-key authentication is enabled.");
        }

        var keyIds = new HashSet<string>(StringComparer.Ordinal);
        var digests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < credentials.Length; index++)
        {
            var credential = credentials[index];
            var prefix = $"Credentials[{index}]";
            if (credential is null)
            {
                failures.Add($"{prefix} must be configured.");
                continue;
            }
            if (!IsBoundedIdentifier(credential.KeyId, 64))
            {
                failures.Add($"{prefix}.KeyId must contain 1-64 visible ASCII characters.");
            }
            else if (!keyIds.Add(credential.KeyId))
            {
                failures.Add($"{prefix}.KeyId must be unique.");
            }

            if (!IsBoundedIdentifier(credential.TenantId, 128))
            {
                failures.Add($"{prefix}.TenantId must contain 1-128 visible ASCII characters.");
            }

            if (!IsSha256Digest(credential.Sha256Digest))
            {
                failures.Add($"{prefix}.Sha256Digest must contain exactly 64 hexadecimal characters.");
            }
            else if (!digests.Add(credential.Sha256Digest))
            {
                failures.Add($"{prefix}.Sha256Digest must be unique.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateStaticFields(ApiKeyAuthOptions options, List<string> failures)
    {
        if (options.PermitLimit is < 1 or > 10_000)
        {
            failures.Add("PermitLimit must be between 1 and 10000.");
        }

        if (options.WindowSeconds is < 1 or > 3_600)
        {
            failures.Add("WindowSeconds must be between 1 and 3600.");
        }
    }

    private static bool IsBoundedIdentifier(string value, int maximumLength) =>
        value.Length is > 0 && value.Length <= maximumLength &&
        value.All(static character => character is >= '!' and <= '~');

    private static bool IsSha256Digest(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
