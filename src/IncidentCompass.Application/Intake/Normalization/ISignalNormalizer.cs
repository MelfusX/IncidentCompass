using IncidentCompass.Application.Intake.IngestSignal;

namespace IncidentCompass.Application.Intake.Normalization;

public interface ISignalNormalizer
{
    IReadOnlyCollection<string> SourceKinds { get; }

    NormalizedSignal Normalize(IngestSignalCommand command, DateTimeOffset receivedAtUtc);
}
