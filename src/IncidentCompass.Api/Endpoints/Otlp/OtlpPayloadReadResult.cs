namespace IncidentCompass.Api;

internal readonly record struct OtlpPayloadReadResult(byte[]? Payload, int? FailureStatusCode)
{
    public static OtlpPayloadReadResult UnsupportedMediaType { get; } = new(null, StatusCodes.Status415UnsupportedMediaType);

    public static OtlpPayloadReadResult PayloadTooLarge { get; } = new(null, StatusCodes.Status413PayloadTooLarge);

    public static OtlpPayloadReadResult Success(byte[] payload) => new(payload, null);
}