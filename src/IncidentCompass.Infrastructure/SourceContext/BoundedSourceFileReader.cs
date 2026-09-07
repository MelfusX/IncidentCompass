using System.Text;

namespace IncidentCompass.Infrastructure.SourceContext;

internal sealed class BoundedSourceFileReader(SourceContextOptions options)
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<SourceFileReadResult> ReadAsync(
        string path,
        int requestedLine,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (file.Length > options.MaxSourceBytes)
        {
            return Rejected("source_file_oversized");
        }

        var bytes = new byte[options.MaxSourceBytes + 1];
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset > options.MaxSourceBytes)
        {
            return Rejected("source_file_oversized");
        }

        Array.Resize(ref bytes, offset);

        if (bytes.AsSpan().Contains((byte)0))
        {
            return Rejected("source_file_binary");
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Rejected("source_file_binary");
        }

        if (string.IsNullOrEmpty(text))
        {
            return Rejected("source_file_empty");
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        if (requestedLine < 1 || requestedLine > lines.Length)
        {
            return Rejected("source_line_invalid");
        }

        var before = (options.MaxExcerptLines - 1) / 2;
        var start = Math.Max(1, requestedLine - before);
        var end = Math.Min(lines.Length, start + options.MaxExcerptLines - 1);
        start = Math.Max(1, end - options.MaxExcerptLines + 1);
        var excerpt = string.Join('\n', lines[(start - 1)..end]);
        return new SourceFileReadResult(excerpt, start, end, "source_match");
    }

    private static SourceFileReadResult Rejected(string code) => new(null, 0, 0, code);
}
