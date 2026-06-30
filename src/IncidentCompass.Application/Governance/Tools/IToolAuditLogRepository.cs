using IncidentCompass.Domain.Governance;

namespace IncidentCompass.Application.Governance.Tools;

public interface IToolAuditLogRepository
{
    Task AddAsync(ToolAuditLogEntry entry, CancellationToken cancellationToken);
}
