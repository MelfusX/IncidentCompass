namespace IncidentCompass.Application.Investigation.Reports;

public sealed class TriageReportValidationException : Exception
{
    public TriageReportValidationException(string message)
        : base(message)
    {
    }
}
