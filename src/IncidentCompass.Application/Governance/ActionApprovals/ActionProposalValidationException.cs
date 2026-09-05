using IncidentCompass.Application.Core.Exceptions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed class ActionProposalValidationException : ValidationException
{
    public ActionProposalValidationException(string message)
        : base(message)
    {
    }

    public ActionProposalValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
