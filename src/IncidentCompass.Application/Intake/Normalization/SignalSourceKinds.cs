namespace IncidentCompass.Application.Intake.Normalization;

internal static class SignalSourceKinds
{
    public const string Tester = "tester";
    public const string User = "user";
    public const string Manual = "manual";
    public const string Otel = "otel";
    public const string Webhook = "webhook"; // valid DTO value but MVP registers no normalizer for it; stays out of Ingestion.AllowedSources
}
