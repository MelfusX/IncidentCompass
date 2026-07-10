namespace IncidentCompass.Application.Intake.FaultGrouping;

internal sealed class FaultGroupingStateChangedException(Guid faultId)
    : Exception($"Fault '{faultId}' changed state while the signal was being grouped.")
{
}
