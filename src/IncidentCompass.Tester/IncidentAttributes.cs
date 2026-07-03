namespace IncidentCompass.Tester;

internal sealed record IncidentAttributes(
    string ErrorType,
    string ErrorMessage,
    string HttpRoute,
    string OperationName,
    int HttpStatusCode);