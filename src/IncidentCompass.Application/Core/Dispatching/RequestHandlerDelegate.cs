namespace IncidentCompass.Application.Core.Dispatching;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
