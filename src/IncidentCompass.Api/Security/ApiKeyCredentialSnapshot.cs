namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyCredentialSnapshot(bool denyAll, IEnumerable<ApiKeyCredential> credentials)
{
    public static ApiKeyCredentialSnapshot Denied { get; } = new(true, []);

    private readonly ApiKeyCredential[] credentials = credentials.ToArray();

    public bool DenyAll { get; } = denyAll;

    public ReadOnlySpan<ApiKeyCredential> Credentials => credentials;
}
