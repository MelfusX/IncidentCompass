using IncidentCompass.Application.Investigation.Reports;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresDocumentationFitResolver
{
    private const string MultipleCurrentDocumentsLimitation =
        "Multiple current documents were cited; their compatibility requires operator review.";
    private const string UnassessableDocumentLimitation =
        "Cited documentation is unversioned or service-mismatched, so its currentness cannot be assessed.";

    public TriageReport ValidateAndApply(
        TriageReport report,
        IReadOnlyList<GroundedReportEvidence> evidence)
    {
        var documents = evidence
            .Where(static item => item.MemoryItemId is not null && item.DocumentationStatus is not null)
            .GroupBy(static item => item.MemoryItemId!.Value)
            .Select(static group => group.First())
            .ToArray();
        var documentationFit = Resolve(documents);
        if (report.DocumentationFit != documentationFit)
        {
            throw new TriageReportValidationException(
                "publish_report documentationFit does not match backend-derived cited-document status.");
        }

        return documentationFit switch
        {
            DocumentationFitStatus.MultipleCurrentDocuments => AddLimitation(report, MultipleCurrentDocumentsLimitation),
            DocumentationFitStatus.Missing when documents.Any(static item =>
                item.DocumentationStatus is "Unversioned" or "ServiceMismatch") =>
                AddLimitation(report, UnassessableDocumentLimitation),
            _ => report
        };
    }

    private static DocumentationFitStatus Resolve(IReadOnlyCollection<GroundedReportEvidence> documents)
    {
        var currentCount = documents.Count(static item => item.DocumentationStatus == "Current");
        var staleCount = documents.Count(static item => item.DocumentationStatus == "Stale");
        return currentCount switch
        {
            > 1 => DocumentationFitStatus.MultipleCurrentDocuments,
            1 when staleCount > 0 => DocumentationFitStatus.CurrentWithHistorical,
            1 => DocumentationFitStatus.Current,
            _ when staleCount > 0 => DocumentationFitStatus.StaleOnly,
            _ => DocumentationFitStatus.Missing
        };
    }

    private static TriageReport AddLimitation(TriageReport report, string limitation)
    {
        return report.Limitations.Contains(limitation, StringComparer.Ordinal)
            ? report
            : report with { Limitations = [.. report.Limitations, limitation] };
    }
}