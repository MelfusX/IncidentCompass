using System.Text;
using FluentValidation;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.Normalization;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Application.Intake.IngestSignal;

internal sealed class IngestSignalCommandValidator : AbstractValidator<IngestSignalCommand>
{
    public IngestSignalCommandValidator(
        ITriageConfigurationRepository configurationRepository,
        SignalNormalizerRegistry normalizerRegistry,
        IOptions<IngestionLimitsOptions> limits)
    {
        RuleFor(command => command.SourceKind).NotEmpty();

        RuleFor(command => command)
            .MustAsync(async (command, cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(command.SourceKind))
                {
                    return true;
                }

                var configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
                return configuration.Ingestion.AllowedSources.Contains(command.SourceKind, StringComparer.Ordinal) &&
                    normalizerRegistry.HasNormalizer(command.SourceKind);
            })
            .WithName("sourceKind")
            .WithMessage(command => $"Source kind '{command.SourceKind}' is not in the configured allowed sources or has no registered normalizer.");

        RuleFor(command => command.Payload)
            .Must(payload => payload is null ||
                Encoding.UTF8.GetByteCount(payload.ToJsonString()) <= limits.Value.MaxPayloadBytes)
            .WithMessage(_ => $"Payload must be {limits.Value.MaxPayloadBytes} bytes or fewer when serialized.");

        RuleFor(command => command.Attributes)
            .Must(attributes => attributes is null ||
                Encoding.UTF8.GetByteCount(attributes.ToJsonString()) <= limits.Value.MaxAttributesBytes)
            .WithMessage(_ => $"Attributes must be {limits.Value.MaxAttributesBytes} bytes or fewer when serialized.");

        RuleFor(command => command)
            .Must(command => !string.IsNullOrWhiteSpace(command.Summary) || !string.IsNullOrWhiteSpace(command.Description))
            .When(command => command.SourceKind is SignalSourceKinds.User or SignalSourceKinds.Manual)
            .WithName("summary")
            .WithMessage("A user or manual report requires a non-blank summary or description.");
    }
}
