using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.Application.Core.Tenancy;

public sealed class ConfigIncidentTenantContext(ITriageConfigurationRepository configurationRepository)
    : IIncidentTenantContext
{
    public async Task<string> GetTenantIdAsync(CancellationToken cancellationToken) =>
        (await configurationRepository.GetCurrentAsync(cancellationToken)).Ingestion.DefaultTenant;
}