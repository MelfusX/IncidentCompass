namespace IncidentCompass.Application.Intake.Fingerprinting;

public sealed record EffectiveFingerprintRule(
    string Id,
    int Version,
    IReadOnlyList<string> Inputs);