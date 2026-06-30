using IncidentCompass.Application.Core.Exceptions;

namespace IncidentCompass.Application.Generation.ModelGateway;

public sealed class ModelRequestValidationException(string message) : ValidationException(message);
