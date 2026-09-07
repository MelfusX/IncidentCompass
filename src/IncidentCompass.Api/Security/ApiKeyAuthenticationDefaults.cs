namespace IncidentCompass.Api.Security;

internal static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "IncidentCompassApiKey";
    public const string HeaderName = "X-IncidentCompass-Key";
    public const string KeyIdClaim = "incidentcompass:key_id";
    public const string TenantIdClaim = "incidentcompass:tenant_id";
}
