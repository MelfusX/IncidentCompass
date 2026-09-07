namespace IncidentCompass.Infrastructure.Configuration;

/// <summary>
/// The resolved PostgreSQL connection settings. <see cref="PostgresOptions.ConnectionStringName"/>
/// names which entry of the <see cref="SectionName"/> section supplies
/// <see cref="ConnectionString"/>, so the deployed keys stay
/// <c>ConnectionStrings:&lt;name&gt;</c> (for example the <c>ConnectionStrings__IncidentCompass</c>
/// environment variable used by <c>.env.example</c> and Docker Compose).
/// </summary>
public sealed class PostgresConnectionOptions
{
    public const string SectionName = "ConnectionStrings";

    public string ConnectionStringName { get; set; } = string.Empty;

    public string? ConnectionString { get; set; }
}
