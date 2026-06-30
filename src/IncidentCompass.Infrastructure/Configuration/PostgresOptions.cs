namespace IncidentCompass.Infrastructure.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "IncidentCompass:Postgres";

    public string ConnectionStringName { get; init; } = "IncidentCompass";
}
