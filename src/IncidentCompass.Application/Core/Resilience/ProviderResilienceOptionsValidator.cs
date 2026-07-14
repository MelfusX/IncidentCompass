using Microsoft.Extensions.Options;

namespace IncidentCompass.Application.Core.Resilience;

internal sealed class ProviderResilienceOptionsValidator : IValidateOptions<ProviderResilienceOptions>
{
    public ValidateOptionsResult Validate(string? name, ProviderResilienceOptions options)
    {
        if (options.FailureThreshold < 1 || options.BackpressureSeconds < 1)
        {
            return ValidateOptionsResult.Fail("Provider resilience failure threshold and backpressure duration must be positive.");
        }

        return ValidateOptionsResult.Success;
    }
}