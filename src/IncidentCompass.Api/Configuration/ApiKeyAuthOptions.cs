namespace IncidentCompass.Api.Configuration;

public sealed class ApiKeyAuthOptions
{
    public const string SectionName = "IncidentCompass:ApiKeyAuth";

    public bool Enabled { get; init; }

    public int PermitLimit { get; init; } = 60;

    public int WindowSeconds { get; init; } = 60;

    public ApiKeyCredentialOptions[] Credentials { get; init; } = [];
}
