using IncidentCompass.Application.Intake.Configuration;

namespace IncidentCompass.IntegrationTests;

internal sealed class ActionDispatchTestConfigurationRepository(TriageConfiguration current)
    : ITriageConfigurationRepository
{
    public TriageConfiguration Current { get; set; } = current;

    public Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Current);

    public Task<TriageConfiguration> GetByHashAsync(
        string configHash,
        CancellationToken cancellationToken) =>
        Task.FromResult(Current with { ConfigHash = configHash });
}
