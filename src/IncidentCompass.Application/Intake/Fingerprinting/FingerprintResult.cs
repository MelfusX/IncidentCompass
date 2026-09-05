using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.Fingerprinting;

internal sealed record FingerprintResult(
    string Value,
    FingerprintStrength Strength,
    EffectiveFingerprintRule EffectiveRule);
