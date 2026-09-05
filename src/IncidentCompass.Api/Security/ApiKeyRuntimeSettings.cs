namespace IncidentCompass.Api.Security;

internal sealed record ApiKeyRuntimeSettings(bool Enabled, int PermitLimit, int WindowSeconds);
