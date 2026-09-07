namespace IncidentCompass.Application.Core.Dispatching;

public delegate Task<TResponse> PipelineContinuation<TResponse>();
