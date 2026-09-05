using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal static class PostgresFaultRowMapper
{
    public const string SelectColumns = """
        id, trigger_signal_id, tenant_id, status, fingerprint, fingerprint_version, fingerprint_strength,
        can_group, service_name, environment, severity, correlation_id, created_at_utc, completed_at_utc,
        recurrence_of, grouping_rule_id, grouping_rule_version
        """;

    public static Fault Map(NpgsqlDataReader reader)
    {
        return new Fault(
            Id: reader.GetGuid(0),
            TriggerSignalId: reader.GetGuid(1),
            TenantId: reader.GetString(2),
            Status: Enum.Parse<FaultStatus>(reader.GetString(3)),
            Fingerprint: reader.GetString(4),
            FingerprintVersion: reader.GetInt32(5),
            FingerprintStrength: Enum.Parse<FingerprintStrength>(reader.GetString(6), ignoreCase: true),
            CanGroup: reader.GetBoolean(7),
            ServiceName: reader.GetString(8),
            Environment: reader.GetString(9),
            Severity: reader.IsDBNull(10) ? null : reader.GetString(10),
            CorrelationId: reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedAtUtc: reader.GetDateTimeOffset(12),
            CompletedAtUtc: reader.IsDBNull(13) ? null : reader.GetDateTimeOffset(13),
            RecurrenceOf: reader.IsDBNull(14) ? null : reader.GetGuid(14))
        {
            GroupingRuleId = reader.GetString(15),
            GroupingRuleVersion = reader.GetInt32(16)
        };
    }
}
