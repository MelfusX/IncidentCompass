using System.Security.Cryptography;
using IncidentCompass.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyCredentialResolver : IDisposable
{
    private readonly IConfiguration configuration;
    private readonly ApiKeyAuthOptionsValidator validator;
    private readonly IDisposable reloadSubscription;
    private ApiKeyCredentialSnapshot snapshot;

    public ApiKeyCredentialResolver(
        IConfiguration configuration,
        ApiKeyAuthOptionsValidator validator,
        ApiKeyRuntimeSettings runtimeSettings)
    {
        this.configuration = configuration;
        this.validator = validator;
        RuntimeSettings = runtimeSettings;

        var initial = BindOptions();
        var validation = validator.Validate(Options.DefaultName, initial);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(ApiKeyAuthOptions),
                validation.Failures);
        }

        snapshot = StaticFieldsChanged(initial)
            ? ApiKeyCredentialSnapshot.Denied
            : CreateSnapshot(initial);
        reloadSubscription = ChangeToken.OnChange(configuration.GetReloadToken, Reload);
    }

    public ApiKeyRuntimeSettings RuntimeSettings { get; }

    public bool AuthenticationRequired => RuntimeSettings.Enabled || Volatile.Read(ref snapshot).DenyAll;

    public bool TryResolve(ReadOnlySpan<byte> digest, out ApiKeyCredential? resolved)
    {
        var current = Volatile.Read(ref snapshot);
        resolved = null;
        if (current.DenyAll)
        {
            return false;
        }

        foreach (var credential in current.Credentials)
        {
            var matches = CryptographicOperations.FixedTimeEquals(digest, credential.Digest);
            if (matches)
            {
                resolved = credential;
            }
        }

        return resolved is not null;
    }

    public void Dispose() => reloadSubscription.Dispose();

    private void Reload()
    {
        try
        {
            var options = BindOptions();
            var validation = validator.Validate(Options.DefaultName, options);
            if (validation.Failed || StaticFieldsChanged(options))
            {
                Interlocked.Exchange(ref snapshot, ApiKeyCredentialSnapshot.Denied);
                return;
            }

            Interlocked.Exchange(ref snapshot, CreateSnapshot(options));
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref snapshot, ApiKeyCredentialSnapshot.Denied);
        }
    }

    private ApiKeyAuthOptions BindOptions() =>
        configuration.GetSection(ApiKeyAuthOptions.SectionName).Get<ApiKeyAuthOptions>() ?? new ApiKeyAuthOptions();

    private bool StaticFieldsChanged(ApiKeyAuthOptions options) =>
        options.Enabled != RuntimeSettings.Enabled ||
        options.PermitLimit != RuntimeSettings.PermitLimit ||
        options.WindowSeconds != RuntimeSettings.WindowSeconds;

    private static ApiKeyCredentialSnapshot CreateSnapshot(ApiKeyAuthOptions options)
    {
        var credentials = (options.Credentials ?? [])
            .Select(static credential => new ApiKeyCredential(
                credential.KeyId,
                credential.TenantId,
                Convert.FromHexString(credential.Sha256Digest)))
            .ToArray();
        return new ApiKeyCredentialSnapshot(false, credentials);
    }
}
