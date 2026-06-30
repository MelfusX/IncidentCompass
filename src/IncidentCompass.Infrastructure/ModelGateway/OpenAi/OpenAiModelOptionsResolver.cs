using IncidentCompass.Application.Generation.ModelGateway;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi;

internal sealed class OpenAiModelOptionsResolver(
    IOptions<OpenAiCompatibleModelClientOptions> options)
{
    public OpenAiCompatibleModelClientOptions Get()
    {
        try
        {
            return options.Value;
        }
        catch (OptionsValidationException exception)
        {
            throw new AiModelException(
                OpenAiModelProvider.Name,
                "OpenAI-compatible model provider configuration is invalid.",
                errorCode: "configuration_error",
                innerException: exception);
        }
    }

    public Uri GetEndpointUri(OpenAiCompatibleModelClientOptions clientOptions)
    {
        if (!clientOptions.IsValid() ||
            !clientOptions.TryCreateEndpointUri(out var endpointUri) ||
            endpointUri is null)
        {
            throw new AiModelException(
                OpenAiModelProvider.Name,
                "OpenAI-compatible model provider configuration is invalid.",
                errorCode: "configuration_error");
        }

        return endpointUri;
    }
}
