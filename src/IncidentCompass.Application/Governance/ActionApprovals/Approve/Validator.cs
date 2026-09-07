using FluentValidation;

namespace IncidentCompass.Application.Governance.ActionApprovals.Approve;

internal sealed class ApproveActionCommandValidator : AbstractValidator<ApproveActionCommand>
{
    public ApproveActionCommandValidator()
    {
        RuleFor(static command => command.PayloadSha256).Matches("^[0-9a-f]{64}$");
        RuleFor(static command => command.ApprovalSha256).Matches("^[0-9a-f]{64}$");
    }
}
