namespace IncidentCompass.Application.Core.Tenancy;

public interface IIncidentTenantContext
{
    Task<string> GetTenantIdAsync(CancellationToken cancellationToken);
}