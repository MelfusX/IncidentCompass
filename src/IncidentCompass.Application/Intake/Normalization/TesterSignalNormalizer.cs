using IncidentCompass.Application.Intake.IngestSignal;

namespace IncidentCompass.Application.Intake.Normalization;

internal sealed class TesterSignalNormalizer : ISignalNormalizer
{
    public IReadOnlyCollection<string> SourceKinds { get; } = [SignalSourceKinds.Tester];

    public NormalizedSignal Normalize(IngestSignalCommand command, DateTimeOffset receivedAtUtc) =>
        StructuredSignalNormalization.Normalize(command, SignalSourceKinds.Tester, receivedAtUtc);
}
