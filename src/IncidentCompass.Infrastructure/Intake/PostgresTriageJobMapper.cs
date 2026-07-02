using IncidentCompass.Domain.Incidents;
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
            LockedUntilUtc: reader.IsDBNull(5) ? null : GetDateTimeOffset(reader, 5),
            NextAttemptAtUtc: reader.IsDBNull(6) ? null : GetDateTimeOffset(reader, 6),
            LastErrorCode: reader.IsDBNull(7) ? null : reader.GetString(7),
            LastErrorMessage: reader.IsDBNull(8) ? null : reader.GetString(8),
            ConfigHash: reader.GetString(9),
            CreatedAtUtc: GetDateTimeOffset(reader, 10),
            UpdatedAtUtc: GetDateTimeOffset(reader, 11));
    }

    public static DateTimeOffset GetDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
