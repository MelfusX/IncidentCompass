using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Configuration;

/// <summary>
/// Binds <see cref="PostgresConnectionOptions"/> from the two-step configuration shape:
/// <see cref="PostgresOptions.ConnectionStringName"/> selects the name, and the value lives at
/// <c>ConnectionStrings:&lt;name&gt;</c>. The <see cref="IConfiguration"/> is captured by
/// <c>AddInfrastructure</c> rather than resolved from the container, so configuration stays out of
/// the service graph while binding remains lazy: the value is read on first
/// <c>IOptions&lt;PostgresConnectionOptions&gt;.Value</c> access, not during composition.
/// </summary>
internal sealed class PostgresConnectionOptionsConfigurator(
    IConfiguration configuration,
    IOptions<PostgresOptions> postgresOptions)
    : IConfigureOptions<PostgresConnectionOptions>
{
    public void Configure(PostgresConnectionOptions options)
    {
        var connectionStringName = postgresOptions.Value.ConnectionStringName;
        options.ConnectionStringName = connectionStringName;
        options.ConnectionString = configuration.GetConnectionString(connectionStringName);
    }
}
