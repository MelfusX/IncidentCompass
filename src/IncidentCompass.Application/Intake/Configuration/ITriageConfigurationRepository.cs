namespace IncidentCompass.Application.Intake.Configuration;

public interface ITriageConfigurationRepository
{
    Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken);

    Task<TriageConfiguration> GetByHashAsync(string configHash, CancellationToken cancellationToken);
}
