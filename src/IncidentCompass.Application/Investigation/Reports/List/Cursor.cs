using System.Buffers.Binary;
using FluentValidation.Results;
using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Investigation.Reports.List;

internal sealed record TriageReportListCursor(DateTimeOffset CreatedAtUtc, Guid ReportId)
{
    public static string Encode(DateTimeOffset createdAtUtc, Guid reportId)
    {
        Span<byte> bytes = stackalloc byte[24];
        BinaryPrimitives.WriteInt64BigEndian(bytes, createdAtUtc.UtcDateTime.Ticks);
        reportId.TryWriteBytes(bytes[8..]);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static TriageReportListCursor? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != 24)
            {
                throw new FormatException();
            }

            return new TriageReportListCursor(
                new DateTimeOffset(new DateTime(BinaryPrimitives.ReadInt64BigEndian(bytes), DateTimeKind.Utc)),
                new Guid(bytes[8..]));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            throw new RequestValidationException([new ValidationFailure("cursor", "is invalid.")]);
        }
    }
}
