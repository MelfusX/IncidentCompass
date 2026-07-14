namespace IncidentCompass.Application.Intake.Configuration;

public sealed record RedactionSettings(
    IReadOnlyCollection<string> AttributeKeys,
    IReadOnlyCollection<RedactionPatternSettings> Patterns,
    IReadOnlyCollection<string> UserIdentifierAttributes)
{
    public static RedactionSettings Default { get; } = new([], [], []);
}
