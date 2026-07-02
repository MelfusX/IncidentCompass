using IncidentCompass.Application.Intake.Configuration;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.Infrastructure.Intake;

// Forces the triage configuration to load (and fail fast on a bad config) and the snapshot to
// persist at host startup in both Api and Worker, matching plan §11 ("resolution + snapshot
// happen at startup"), without either process needing its own explicit call.
internal sealed class TriageConfigurationWarmupHostedService(ITriageConfigurationRepository configurationRepository)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        configurationRepository.GetCurrentAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
