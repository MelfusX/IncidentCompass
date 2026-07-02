using IncidentCompass.Application.Intake.IngestSignal;

namespace IncidentCompass.Application.Intake.Normalization;

internal sealed class OtelShapedSignalNormalizer : ISignalNormalizer
{
    public IReadOnlyCollection<string> SourceKinds { get; } = [SignalSourceKinds.Otel];

    public NormalizedSignal Normalize(IngestSignalCommand command, DateTimeOffset receivedAtUtc) =>
        StructuredSignalNormalization.Normalize(command, SignalSourceKinds.Otel, receivedAtUtc);
}
