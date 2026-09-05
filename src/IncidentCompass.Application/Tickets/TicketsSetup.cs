using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Governance.PostReportActions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentCompass.Application.Tickets;

internal static class TicketsSetup
{
    public static IServiceCollection AddTicketsCore(this IServiceCollection services)
    {
        services.TryAddScoped<ITicketSearch, UnavailableTicketSearch>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IImmediateAgentTool, TicketSearchTool>());
        services.AddSingleton(new AgentToolDescriptor("ticket_search", AgentToolCapability.ImmediateRead));
        services.AddSingleton(TicketCreateTool.Descriptor);
        services.AddSingleton(TicketUpdatePostReportActionWorkflow.Descriptor);
        return services;
    }
}
