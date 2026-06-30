using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Core.Health;

public sealed record GetHealthStatusQuery(string Component) : IRequest<HealthStatus>;
