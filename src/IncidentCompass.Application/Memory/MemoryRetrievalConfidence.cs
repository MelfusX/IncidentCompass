namespace IncidentCompass.Application.Memory;

internal static class MemoryRetrievalConfidence
{
    public static string Band(double score) => score switch
    {
        >= 0.8 => "high",
        >= 0.5 => "medium",
        _ => "low"
    };
}
