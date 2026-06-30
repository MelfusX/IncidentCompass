using IncidentCompass.Application.Core.Security;

namespace IncidentCompass.Infrastructure.Security;

internal sealed class SystemUserContext : IBackgroundUserContext
{
    public bool IsAuthenticated => true;

    public string? UserId => "system";

    public string? TenantId => null;

    public IReadOnlyCollection<string> Roles { get; } = ["system"];

    public IReadOnlyCollection<string> Groups { get; } = [];
}
