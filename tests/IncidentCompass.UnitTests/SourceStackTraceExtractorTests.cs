using System.Globalization;
using System.Text.Json;
using IncidentCompass.Application.SourceContext;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.UnitTests;

public sealed class SourceStackTraceExtractorTests
{
    private static readonly string[] NonFrameStringArray = ["not", "a string"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Extract_ParsesEverySupportedLocation(int location)
    {
        var attributes = new Dictionary<string, object?>();
        var body = new Dictionary<string, object?>();
        string? description = null;
        string? errorMessage = null;
        var frame = Frame("src/Supported.cs", 23);
        switch (location)
        {
            case 0: attributes["exception.stacktrace"] = frame; break;
            case 1: attributes["exception.stack_trace"] = frame; break;
            case 2: body["stackTrace"] = frame; break;
            case 3: body["exception.stacktrace"] = frame; break;
            case 4: description = frame; break;
            case 5: errorMessage = frame; break;
        }

        var extracted = Assert.Single(SourceStackTraceExtractor.Extract(
            CreateSignal(attributes, body, description, errorMessage)));

        Assert.Equal("src/Supported.cs", extracted.Path);
        Assert.Equal(23, extracted.LineNumber);
    }

    [Fact]
    public void Extract_UsesFixedFieldOrderAndCapsFrames()
    {
        var lowerPriority = Frame("body.cs", 7);
        var preferred = string.Join('\n', Enumerable.Range(1, 10).Select(index => Frame($"src/File{index}.cs", index)));
        var signal = CreateSignal(
            new Dictionary<string, object?> { ["exception.stacktrace"] = preferred },
            new Dictionary<string, object?> { ["stackTrace"] = lowerPriority },
            lowerPriority,
            lowerPriority);

        var frames = SourceStackTraceExtractor.Extract(signal);

        Assert.Equal(8, frames.Count);
        Assert.Equal("src/File1.cs", frames[0].Path);
        Assert.Equal(1, frames[0].LineNumber);
        Assert.Equal("src/File8.cs", frames[^1].Path);
    }

    [Fact]
    public void Extract_IgnoresNonStringsAndFallsBackThroughAllLocations()
    {
        var signal = CreateSignal(
            new Dictionary<string, object?>
            {
                ["exception.stacktrace"] = NonFrameStringArray,
                ["exception.stack_trace"] = "not a frame"
            },
            new Dictionary<string, object?>
            {
                ["stackTrace"] = 42,
                ["exception.stacktrace"] = Frame("src/Body.cs", 19)
            },
            Frame("src/Description.cs", 20),
            Frame("src/Error.cs", 21));

        var frame = Assert.Single(SourceStackTraceExtractor.Extract(signal));

        Assert.Equal("src/Body.cs", frame.Path);
        Assert.Equal(19, frame.LineNumber);
    }

    [Fact]
    public void Extract_ReadsAtMostThirtyTwoKiBFromAField()
    {
        var tooLate = new string('x', SourceStackTraceExtractor.MaxFieldCharacters) + "\n" + Frame("late.cs", 9);
        var signal = CreateSignal(
            new Dictionary<string, object?> { ["exception.stacktrace"] = tooLate },
            new Dictionary<string, object?>(),
            null,
            Frame("fallback.cs", 4));

        var frame = Assert.Single(SourceStackTraceExtractor.Extract(signal));

        Assert.Equal("fallback.cs", frame.Path);
    }

    private static string Frame(string path, int line) => $"   at Example.Run() in {path}:line {line}";

    private static Signal CreateSignal(
        IReadOnlyDictionary<string, object?> attributes,
        IReadOnlyDictionary<string, object?> body,
        string? description,
        string? errorMessage)
    {
        var now = DateTimeOffset.Parse("2026-08-28T00:00:00Z", CultureInfo.InvariantCulture);
        return new Signal(
            Guid.NewGuid(), "tenant", "otel", null, null, null, FingerprintStrength.Strong, true,
            null, false, null, null, null, null, null, "checkout", "test", null, "Error",
            "ExampleException", errorMessage, "summary", description, null, null, null, null,
            JsonSerializer.SerializeToElement(attributes), JsonSerializer.SerializeToElement(body),
            now, now, null);
    }
}
