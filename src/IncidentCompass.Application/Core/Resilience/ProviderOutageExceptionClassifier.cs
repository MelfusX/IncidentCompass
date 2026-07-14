using IncidentCompass.Application.Core.Errors;

namespace IncidentCompass.Application.Core.Resilience;

internal static class ProviderOutageExceptionClassifier
{
    public static bool IsProviderOutage(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ProviderException)
            {
                return true;
            }
        }

        return false;
    }
}