using IncidentCompass.Application.Core.Exceptions;

namespace IncidentCompass.Application.Core.ModelGateway;

public sealed class ModelRequestValidationException(string message) : ValidationException(message);
