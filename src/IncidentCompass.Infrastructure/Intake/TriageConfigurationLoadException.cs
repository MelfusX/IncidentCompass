namespace IncidentCompass.Infrastructure.Intake;

internal sealed class TriageConfigurationLoadException : InvalidOperationException
{
    private TriageConfigurationLoadException(string message)
        : base(message)
    {
    }

    private TriageConfigurationLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static TriageConfigurationLoadException UnsupportedKind(string kind)
    {
        return new TriageConfigurationLoadException(
            $"Triage configuration source kind '{kind}' is not supported (only 'File' is supported in Phase 1).");
    }

    public static TriageConfigurationLoadException ConfigFileMissing(string path)
    {
        return new TriageConfigurationLoadException(
            $"Triage configuration file was not found at '{path}'.");
    }

    public static TriageConfigurationLoadException ReferencedFileMissing(string refValue, string resolvedPath)
    {
        return new TriageConfigurationLoadException(
            $"Triage configuration references '{refValue}' which resolves to a missing file at '{resolvedPath}'.");
    }

    public static TriageConfigurationLoadException InvalidJson(string path, Exception innerException)
    {
        return new TriageConfigurationLoadException(
            $"Triage configuration file at '{path}' is not valid JSON.",
            innerException);
    }

    public static TriageConfigurationLoadException InvalidSetting(string name, string value, string expected)
    {
        return new TriageConfigurationLoadException(
            $"Triage configuration setting '{name}' has unsupported value '{value}'. Expected {expected}.");
    }
}
