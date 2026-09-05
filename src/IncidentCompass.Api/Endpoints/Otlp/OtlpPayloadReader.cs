using System.Buffers;
using IncidentCompass.Application.Intake.Configuration;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Api;

internal sealed class OtlpPayloadReader(IOptions<IngestionLimitsOptions> limits)
{
    public async Task<OtlpPayloadReadResult> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var maxPayloadBytes = limits.Value.MaxPayloadBytes;
        if (!IsProtobuf(request.ContentType) || request.Headers.ContentEncoding.Count > 0)
        {
            return OtlpPayloadReadResult.UnsupportedMediaType;
        }

        if (request.ContentLength is long contentLength && contentLength > maxPayloadBytes)
        {
            return OtlpPayloadReadResult.PayloadTooLarge;
        }

        await using var payload = new MemoryStream(maxPayloadBytes);
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min((long)maxPayloadBytes + 1, 81920));
        try
        {
            var totalBytes = 0;
            while (true)
            {
                var bytesRead = await request.Body.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    return OtlpPayloadReadResult.Success(payload.ToArray());
                }

                if (bytesRead > maxPayloadBytes - totalBytes)
                {
                    return OtlpPayloadReadResult.PayloadTooLarge;
                }

                await payload.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytes += bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsProtobuf(string? contentType) =>
        string.Equals(contentType, "application/x-protobuf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "application/protobuf", StringComparison.OrdinalIgnoreCase);
}
