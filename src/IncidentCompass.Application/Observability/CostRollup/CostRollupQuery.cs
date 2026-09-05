using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Observability.CostRollup;

public sealed record CostRollupQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc) : IRequest<CostRollupResponse>;
