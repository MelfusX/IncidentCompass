using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Core.Tenancy;
using IncidentCompass.Application.Intake.FaultGrouping;

namespace IncidentCompass.Application.Intake.GetFault;

public sealed class GetFaultQueryHandler(IFaultRepository faultRepository, ITriageJobRepository triageJobRepository, IIncidentTenantContext incidentTenantContext)
    : IRequestHandler<GetFaultQuery, FaultDetailsResponse>
{
    public async Task<FaultDetailsResponse> HandleAsync(GetFaultQuery request, CancellationToken cancellationToken)
    {
        var fault = await faultRepository.FindByIdAsync(request.FaultId, await incidentTenantContext.GetTenantIdAsync(cancellationToken), cancellationToken)
            ?? throw new NotFoundException(
                $"Fault '{request.FaultId}' was not found.",
                ApplicationErrorCodes.FaultNotFound,
                "The requested fault does not exist.");
        var job = await triageJobRepository.FindByFaultIdAsync(fault.Id, cancellationToken);
        return new FaultDetailsResponse(
            fault.Id, fault.Status.ToString(), fault.Fingerprint, fault.FingerprintVersion,
            fault.FingerprintStrength.ToString(), fault.CanGroup, fault.ServiceName, fault.Environment,
            fault.Severity, fault.CorrelationId, fault.TriggerSignalId, fault.RecurrenceOf,
            fault.CreatedAtUtc, fault.CompletedAtUtc,
            job is null ? null : new TriageJobSummary(job.Id, job.Status.ToString(), job.Attempt, job.ConfigHash, job.CreatedAtUtc));
    }
}
