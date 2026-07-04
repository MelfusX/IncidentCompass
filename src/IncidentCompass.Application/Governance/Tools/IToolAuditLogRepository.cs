using IncidentCompass.Domain.Governance;

namespace IncidentCompass.Application.Governance.Tools;

/// DORMANT: reserved for IC-BL-010 governed tool execution work.
public interface IToolAuditLogRepository
{
    Task AddAsync(ToolAuditLogEntry entry, CancellationToken cancellationToken);
}
