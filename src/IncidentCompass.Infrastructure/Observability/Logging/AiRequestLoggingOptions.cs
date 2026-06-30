using System.ComponentModel.DataAnnotations;

namespace IncidentCompass.Infrastructure.Observability.Logging;

public sealed class AiRequestLoggingOptions
{
    public const string SectionName = "IncidentCompass:Observability:AiRequestLogging";

    [EnumDataType(typeof(AiRequestLoggingFailureMode))]
    public AiRequestLoggingFailureMode FailureMode { get; init; } = AiRequestLoggingFailureMode.FailOpen;
}
