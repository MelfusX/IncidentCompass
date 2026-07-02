using IncidentCompass.Domain.Exceptions;

namespace IncidentCompass.Application.Intake.Normalization;

public sealed class SignalNormalizerRegistry
{
    private readonly Dictionary<string, ISignalNormalizer> _normalizersBySourceKind;

    public SignalNormalizerRegistry(IEnumerable<ISignalNormalizer> normalizers)
    {
        _normalizersBySourceKind = new Dictionary<string, ISignalNormalizer>(StringComparer.Ordinal);
        foreach (var normalizer in normalizers)
        {
            foreach (var sourceKind in normalizer.SourceKinds)
            {
                _normalizersBySourceKind[sourceKind] = normalizer;
            }
        }
    }

    public bool HasNormalizer(string sourceKind) => _normalizersBySourceKind.ContainsKey(sourceKind);

    public ISignalNormalizer Resolve(string sourceKind)
    {
        if (_normalizersBySourceKind.TryGetValue(sourceKind, out var normalizer))
        {
            return normalizer;
        }

        throw new InvariantViolationException(
            $"No signal normalizer is registered for source kind '{sourceKind}'. This indicates the source " +
            "was allowed in configuration without a registered normalizer -- should be unreachable given the " +
            "config's AllowedSources list is expected to only name registered sources.");
    }
}
