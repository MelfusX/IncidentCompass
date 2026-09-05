using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyCredentialResolver resolver,
    ApiAuthenticationMetrics metrics)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!resolver.AuthenticationRequired)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var values))
        {
            metrics.RecordRejection(ApiAuthenticationRejectionOutcome.Missing);
            return Task.FromResult(AuthenticateResult.Fail("API key is missing."));
        }

        if (values.Count != 1 || !IsValidPresentedKey(values[0]))
        {
            metrics.RecordRejection(ApiAuthenticationRejectionOutcome.Malformed);
            return Task.FromResult(AuthenticateResult.Fail("API key is malformed."));
        }

        var presentedKey = values[0]!;
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(presentedKey));
        if (!resolver.TryResolve(digest, out var credential))
        {
            metrics.RecordRejection(ApiAuthenticationRejectionOutcome.Invalid);
            return Task.FromResult(AuthenticateResult.Fail("API key is invalid."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, credential!.KeyId),
            new Claim(ApiKeyAuthenticationDefaults.KeyIdClaim, credential.KeyId),
            new Claim(ApiKeyAuthenticationDefaults.TenantIdClaim, credential.TenantId)
        };
        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool IsValidPresentedKey(string? value)
    {
        if (value is null || value.Length is < 32 or > 128)
        {
            return false;
        }

        return value.All(static character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }
}
