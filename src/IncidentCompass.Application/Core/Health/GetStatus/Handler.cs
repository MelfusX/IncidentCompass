using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Configuration;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Application.Core.Health;

public sealed class GetHealthStatusHandler(
    TimeProvider timeProvider,
    IOptions<ApplicationOptions> options)
    : IRequestHandler<GetHealthStatusQuery, HealthStatus>
{
    public Task<HealthStatus> HandleAsync(
        GetHealthStatusQuery request,
        CancellationToken cancellationToken)
    {
        var status = new HealthStatus(
            Status: "Healthy",
            Component: request.Component,
            ApiVersion: options.Value.ApiVersion,
            CheckedAtUtc: timeProvider.GetUtcNow());

        return Task.FromResult(status);
    }
}
