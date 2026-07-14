using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal static class PostgresTriageJobMapper
{
    public static TriageJob Map(NpgsqlDataReader reader)
    {
        return new TriageJob(
            Id: reader.GetGuid(0),
            FaultId: reader.GetGuid(1),
            Status: Enum.Parse<TriageJobStatus>(reader.GetString(2)),
            Attempt: reader.GetInt32(3),
            LockedBy: reader.IsDBNull(4) ? null : reader.GetString(4),
            LockedUntilUtc: reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
            NextAttemptAtUtc: reader.IsDBNull(6) ? null : reader.GetDateTimeOffset(6),
            LastErrorCode: reader.IsDBNull(7) ? null : reader.GetString(7),
            LastErrorMessage: reader.IsDBNull(8) ? null : reader.GetString(8),
            ConfigHash: reader.GetString(9),
            CreatedAtUtc: reader.GetDateTimeOffset(10),
            UpdatedAtUtc: reader.GetDateTimeOffset(11))
        {
            ReTriageTriggerJobId = reader.IsDBNull(12) ? null : reader.GetGuid(12),
            SupersedesReportId = reader.IsDBNull(13) ? null : reader.GetGuid(13)
        };
    }
}
