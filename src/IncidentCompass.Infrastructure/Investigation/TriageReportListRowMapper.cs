using IncidentCompass.Application.Investigation.Reports.List;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

/// <summary>
/// Resolves the report-list column ordinals by name once per reader, so reordering the SELECT list
/// cannot silently shift the mapped fields. Only the columns the schema declares nullable are
/// guarded: <c>is_mass_issue</c>, <c>recommended_next_action</c> and <c>supersedes_report_id</c> are
/// <c>NULL</c>-able on <c>incidentcompass.triage_reports</c>, and <c>superseded_by_report_id</c> comes
/// from a LEFT JOIN LATERAL that yields no row when the report has no successor. The remaining
/// columns are NOT NULL, so a null there is a data defect that must surface rather than be coerced.
/// </summary>
internal sealed class TriageReportListRowMapper
{
    private readonly int id;
    private readonly int faultId;
    private readonly int status;
    private readonly int summary;
    private readonly int classification;
    private readonly int confidence;
    private readonly int isMassIssue;
    private readonly int recommendedNextAction;
    private readonly int createdAtUtc;
    private readonly int serviceName;
    private readonly int environment;
    private readonly int supersedesReportId;
    private readonly int supersededByReportId;

    public TriageReportListRowMapper(NpgsqlDataReader reader)
    {
        id = reader.GetOrdinal("report_id");
        faultId = reader.GetOrdinal("fault_id");
        status = reader.GetOrdinal("status");
        summary = reader.GetOrdinal("summary");
        classification = reader.GetOrdinal("classification");
        confidence = reader.GetOrdinal("confidence");
        isMassIssue = reader.GetOrdinal("is_mass_issue");
        recommendedNextAction = reader.GetOrdinal("recommended_next_action");
        createdAtUtc = reader.GetOrdinal("created_at_utc");
        serviceName = reader.GetOrdinal("service_name");
        environment = reader.GetOrdinal("environment");
        supersedesReportId = reader.GetOrdinal("supersedes_report_id");
        supersededByReportId = reader.GetOrdinal("superseded_by_report_id");
    }

    public TriageReportListItemResponse Map(NpgsqlDataReader reader)
    {
        var successorId = reader.IsDBNull(supersededByReportId) ? null : (Guid?)reader.GetGuid(supersededByReportId);
        return new TriageReportListItemResponse(
            reader.GetGuid(id),
            reader.GetGuid(faultId),
            reader.GetString(status),
            reader.GetString(summary),
            reader.GetString(classification),
            reader.GetString(confidence),
            reader.IsDBNull(isMassIssue) ? null : reader.GetBoolean(isMassIssue),
            reader.IsDBNull(recommendedNextAction) ? string.Empty : reader.GetString(recommendedNextAction),
            reader.GetDateTimeOffset(createdAtUtc),
            reader.GetString(serviceName),
            reader.GetString(environment),
            reader.IsDBNull(supersedesReportId) ? null : reader.GetGuid(supersedesReportId),
            successorId,
            successorId is null);
    }
}
