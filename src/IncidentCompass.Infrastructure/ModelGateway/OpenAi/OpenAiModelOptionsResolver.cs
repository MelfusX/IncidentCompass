using IncidentCompass.Application.Core.ModelGateway;
using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.OpenAiCompatible;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.ModelGateway.OpenAi;

internal sealed class OpenAiModelOptionsResolver(
    IOptions<OpenAiCompatibleModelClientOptions> options)
{
    public OpenAiCompatibleModelClientOptions Get()
    {
        return OpenAiCompatibleOptionsResolver.Get(
            options,
            exception => new AiModelException(
                OpenAiModelProvider.Name,
                "OpenAI-compatible model provider configuration is invalid.",
                errorCode: "configuration_error",
                innerException: exception));
    }

    public Uri GetEndpointUri(OpenAiCompatibleModelClientOptions clientOptions)
    {
        return OpenAiCompatibleOptionsResolver.GetEndpointUri(
            clientOptions.IsValid(),
            clientOptions.TryCreateEndpointUri(out var endpointUri),
            endpointUri,
            () => new AiModelException(
                OpenAiModelProvider.Name,
                "OpenAI-compatible model provider configuration is invalid.",
                errorCode: "configuration_error"));
    }
}
