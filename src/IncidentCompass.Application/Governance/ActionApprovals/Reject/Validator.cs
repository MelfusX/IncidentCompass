using FluentValidation;

namespace IncidentCompass.Application.Governance.ActionApprovals.Reject;

internal sealed class RejectActionCommandValidator : AbstractValidator<RejectActionCommand>
{
    public RejectActionCommandValidator()
    {
        RuleFor(static command => command.PayloadSha256).Matches("^[0-9a-f]{64}$");
        RuleFor(static command => command.ApprovalSha256).Matches("^[0-9a-f]{64}$");
        When(static command => command.Reason is not null, () =>
        {
            RuleFor(static command => command.Reason)
                .NotEmpty()
                .MaximumLength(ActionApprovalLimits.MaximumRejectionReasonCharacters);
        });
    }
}
