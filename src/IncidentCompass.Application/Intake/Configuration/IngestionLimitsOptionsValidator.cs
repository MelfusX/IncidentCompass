using Microsoft.Extensions.Options;

namespace IncidentCompass.Application.Intake.Configuration;

internal sealed class IngestionLimitsOptionsValidator : IValidateOptions<IngestionLimitsOptions>
{
    private const int MinimumPayloadBytes = 1024;
    private const int MaximumPayloadBytes = 1_048_576;

    public ValidateOptionsResult Validate(string? name, IngestionLimitsOptions options)
    {
        if (options.MaxPayloadBytes < MinimumPayloadBytes || options.MaxPayloadBytes > MaximumPayloadBytes)
        {
            return ValidateOptionsResult.Fail($"MaxPayloadBytes must be between {MinimumPayloadBytes} and {MaximumPayloadBytes}.");
        }

        if (options.MaxAttributesBytes < 1 || options.MaxAttributesBytes > options.MaxPayloadBytes)
        {
            return ValidateOptionsResult.Fail("MaxAttributesBytes must be positive and no greater than MaxPayloadBytes.");
        }

        return ValidateOptionsResult.Success;
    }
}
