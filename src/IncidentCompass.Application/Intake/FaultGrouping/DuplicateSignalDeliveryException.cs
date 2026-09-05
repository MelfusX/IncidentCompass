namespace IncidentCompass.Application.Intake.FaultGrouping;

public sealed class DuplicateSignalDeliveryException : Exception
{
    public DuplicateSignalDeliveryException()
        : base("The signal delivery was already accepted.")
    {
    }
}
