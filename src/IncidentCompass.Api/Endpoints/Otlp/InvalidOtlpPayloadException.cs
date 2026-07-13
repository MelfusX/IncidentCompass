namespace IncidentCompass.Api;

internal sealed class InvalidOtlpPayloadException(string message) : Exception(message);