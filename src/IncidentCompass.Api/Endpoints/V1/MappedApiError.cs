namespace IncidentCompass.Api;

/// <summary>
/// The API response built for a mapped exception, plus the stable error code it carries. The
/// code travels alongside the response so the API error boundary can log it without re-deriving
/// it or parsing the response body.
/// </summary>
internal readonly record struct MappedApiError(IResult Result, string Code);
