namespace IncidentCompass.Api.Configuration;

public sealed class ApiKeyCredentialOptions
{
    public string KeyId { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string Sha256Digest { get; init; } = string.Empty;
}
