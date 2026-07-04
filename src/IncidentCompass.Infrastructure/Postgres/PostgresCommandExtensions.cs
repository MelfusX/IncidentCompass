using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Postgres;

internal static class PostgresCommandExtensions
{
    public static void AddParameter(this NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static void AddJsonbParameter(this NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, NpgsqlDbType.Jsonb, value is null ? DBNull.Value : value);
    }

    public static DateTimeOffset GetDateTimeOffset(this NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        return value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    public static string ToDbString<TEnum>(this TEnum value)
        where TEnum : struct, Enum => value.ToString();

    public static string ToLowerDbString<TEnum>(this TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();
}
