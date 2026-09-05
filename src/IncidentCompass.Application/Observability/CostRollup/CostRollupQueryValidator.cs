using FluentValidation;

namespace IncidentCompass.Application.Observability.CostRollup;

public sealed class CostRollupQueryValidator : AbstractValidator<CostRollupQuery>
{
    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    public CostRollupQueryValidator()
    {
        RuleFor(static request => request.FromUtc)
            .Must(static value => value.Offset == TimeSpan.Zero)
            .WithMessage("must use a UTC offset.");
        RuleFor(static request => request.ToUtc)
            .Must(static value => value.Offset == TimeSpan.Zero)
            .WithMessage("must use a UTC offset.");
        RuleFor(static request => request)
            .Custom(static (request, context) =>
            {
                if (request.ToUtc <= request.FromUtc)
                {
                    context.AddFailure("window", "toUtc must be later than fromUtc.");
                    return;
                }

                if (request.ToUtc - request.FromUtc > MaximumWindow)
                {
                    context.AddFailure("window", "must not exceed 31 days.");
                }
            });
    }
}
