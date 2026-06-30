using IncidentCompass.Application.Generation.ModelGateway;
using IncidentCompass.Application.Core.ModelClients;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Configuration;

internal sealed class ModelGatewayProviderOptionsValidator : IValidateOptions<ModelGatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelGatewayOptions options)
    {
        return ProviderKindParser.TryParse(options.Provider, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Model gateway provider '{options.Provider}' is unsupported.");
    }
}
