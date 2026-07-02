namespace IncidentCompass.Application.Intake.Configuration;

public interface ITriageConfigurationRepository
{
    Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken);
}
