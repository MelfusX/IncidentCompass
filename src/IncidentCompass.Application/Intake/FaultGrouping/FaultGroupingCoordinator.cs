using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Exceptions;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed class FaultGroupingCoordinator(
    ISignalRepository signalRepository,
    IFaultRepository faultRepository,
    ITriageJobRepository triageJobRepository,
    IIntakeUnitOfWork intakeUnitOfWork,
    GroundedFactsAssembler groundedFactsAssembler,
    TimeProvider timeProvider)
{
    public async Task<FaultGroupingOutcome> ResolveAsync(
        Signal draftSignal,
        TriageConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var settings = configuration.FaultGrouping;
        var now = timeProvider.GetUtcNow();

        if (draftSignal.FingerprintStrength == FingerprintStrength.Weak)
        {
            return await CreateNewFaultAsync(draftSignal, recurrenceOfFaultId: null, configuration, now, cancellationToken);
        }

        var openFault = await faultRepository.FindOpenFaultAsync(
            draftSignal.TenantId, draftSignal.ServiceName, draftSignal.Environment,
            draftSignal.Fingerprint!, draftSignal.FingerprintVersion!.Value, cancellationToken);
        if (openFault is not null)
        {
            var finalSignal = draftSignal with { FaultId = openFault.Id };
            await signalRepository.InsertAsync(finalSignal, cancellationToken);
            return new FaultGroupingOutcome(openFault, Job: null, IsNewFault: false, IsNewJob: false, IsSuppressed: false);
        }

        var closedFault = await faultRepository.FindMostRecentClosedFaultAsync(
            draftSignal.TenantId, draftSignal.ServiceName, draftSignal.Environment,
            draftSignal.Fingerprint!, draftSignal.FingerprintVersion!.Value, cancellationToken);
        if (closedFault is not null &&
            closedFault.CompletedAtUtc is not null &&
            closedFault.CompletedAtUtc.Value >= now.AddMinutes(-settings.SilenceWindowMinutes))
        {
            var finalSignal = draftSignal with
            {
                FaultId = closedFault.Id,
                IsSuppressed = true,
                SuppressedByFaultId = closedFault.Id,
                SuppressionReason = "silence_window",
            };
            await signalRepository.InsertAsync(finalSignal, cancellationToken);
            return new FaultGroupingOutcome(closedFault, Job: null, IsNewFault: false, IsNewJob: false, IsSuppressed: true);
        }

        return await CreateNewFaultAsync(draftSignal, closedFault?.Id, configuration, now, cancellationToken);
    }

    private async Task<FaultGroupingOutcome> CreateNewFaultAsync(
        Signal draftSignal,
        Guid? recurrenceOfFaultId,
        TriageConfiguration configuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await intakeUnitOfWork.ExecuteAsync(
            currentCancellationToken => CreateNewFaultCoreAsync(
                draftSignal,
                recurrenceOfFaultId,
                configuration,
                now,
                currentCancellationToken),
            cancellationToken);
    }

    private async Task<FaultGroupingOutcome> CreateNewFaultCoreAsync(
        Signal draftSignal,
        Guid? recurrenceOfFaultId,
        TriageConfiguration configuration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var signalWithoutFault = draftSignal with { FaultId = null };
        await signalRepository.InsertAsync(signalWithoutFault, cancellationToken);

        var candidateFault = new Fault(
            Id: Guid.NewGuid(),
            TriggerSignalId: draftSignal.Id,
            TenantId: draftSignal.TenantId,
            Status: FaultStatus.Queued,
            Fingerprint: draftSignal.Fingerprint!,
            FingerprintVersion: draftSignal.FingerprintVersion!.Value,
            FingerprintStrength: draftSignal.FingerprintStrength,
            CanGroup: draftSignal.CanGroup,
            ServiceName: draftSignal.ServiceName,
            Environment: draftSignal.Environment,
            Severity: draftSignal.Severity,
            CorrelationId: draftSignal.ExternalId,
            CreatedAtUtc: now,
            CompletedAtUtc: null,
            RecurrenceOf: recurrenceOfFaultId);

        var insertedFault = await faultRepository.TryInsertAsync(candidateFault, cancellationToken);
        if (insertedFault is null)
        {
            var winningFault = await AttachToRaceWinnerAsync(draftSignal, cancellationToken);
            return new FaultGroupingOutcome(winningFault, Job: null, IsNewFault: false, IsNewJob: false, IsSuppressed: false);
        }

        var fault = insertedFault;
        await signalRepository.AttachToFaultAsync(draftSignal.Id, fault.Id, cancellationToken);
        var job = await triageJobRepository.InsertPendingAsync(fault.Id, configuration.ConfigHash, cancellationToken);

        var neighborCount = draftSignal.CanGroup
            ? await CountNeighborsAsync(draftSignal, configuration.FaultGrouping, cancellationToken)
            : 0;
        var isMassIssue = DetermineIsMassIssue(draftSignal, neighborCount, configuration.FaultGrouping);

        var finalSignal = signalWithoutFault with { FaultId = fault.Id };
        await groundedFactsAssembler.AssembleAsync(job, finalSignal, fault, neighborCount, isMassIssue, configuration.FaultGrouping, cancellationToken);

        return new FaultGroupingOutcome(fault, job, IsNewFault: true, IsNewJob: true, IsSuppressed: false);
    }

    private async Task<Fault> AttachToRaceWinnerAsync(Signal draftSignal, CancellationToken cancellationToken)
    {
        // Lost the race to a concurrent strong-signal insert; re-read the winner and attach there instead.
        var winningFault = await faultRepository.FindOpenFaultAsync(
            draftSignal.TenantId, draftSignal.ServiceName, draftSignal.Environment,
            draftSignal.Fingerprint!, draftSignal.FingerprintVersion!.Value, cancellationToken)
            ?? throw new InvariantViolationException("Lost the fault-creation race but no open fault was found afterward.");
        await signalRepository.AttachToFaultAsync(draftSignal.Id, winningFault.Id, cancellationToken);
        return winningFault;
    }

    private async Task<int> CountNeighborsAsync(Signal draftSignal, FaultGroupingSettings settings, CancellationToken cancellationToken)
    {
        var windowStart = draftSignal.ObservedAtUtc.AddMinutes(-settings.LookbackMinutes);
        return await signalRepository.CountDistinctNeighborsAsync(
            draftSignal.TenantId, draftSignal.ServiceName, draftSignal.Environment,
            draftSignal.Fingerprint!, draftSignal.FingerprintVersion!.Value,
            windowStart, draftSignal.ObservedAtUtc, cancellationToken);
    }

    private static bool? DetermineIsMassIssue(Signal draftSignal, int neighborCount, FaultGroupingSettings settings)
    {
        if (!draftSignal.CanGroup)
        {
            return null;
        }

        return neighborCount >= settings.MassIssue.MinNeighborCount &&
            draftSignal.FingerprintStrength >= settings.MassIssue.GetMinimumFingerprintStrength();
    }
}
