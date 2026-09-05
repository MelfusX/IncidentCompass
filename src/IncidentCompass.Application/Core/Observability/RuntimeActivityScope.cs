using System.Diagnostics;

namespace IncidentCompass.Application.Core.Observability;

internal sealed class RuntimeActivityScope(Activity? activity) : IDisposable
{
    public void Dispose()
    {
        try
        {
            activity?.Dispose();
        }
        catch
        {
        }
    }
}
