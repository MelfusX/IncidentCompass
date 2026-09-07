using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.OpenAiCompatible;

internal static class OpenAiCompatibleOptionsResolver
{
    public static TOptions Get<TOptions>(
        IOptions<TOptions> options,
        Func<OptionsValidationException, Exception> createInvalidConfigurationException)
        where TOptions : class
    {
        try
        {
            return options.Value;
        }
        catch (OptionsValidationException exception)
        {
            throw createInvalidConfigurationException(exception);
        }
    }

    public static Uri GetEndpointUri(
        bool isValid,
        bool endpointCreated,
        Uri? endpointUri,
        Func<Exception> createInvalidConfigurationException)
    {
        if (!isValid || !endpointCreated || endpointUri is null)
        {
            throw createInvalidConfigurationException();
        }

        return endpointUri;
    }
}
