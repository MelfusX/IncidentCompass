namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyCredential(string keyId, string tenantId, ReadOnlySpan<byte> digest)
{
    private readonly byte[] digest = digest.ToArray();

    public string KeyId { get; } = keyId;

    public string TenantId { get; } = tenantId;

    public ReadOnlySpan<byte> Digest => digest;
}
