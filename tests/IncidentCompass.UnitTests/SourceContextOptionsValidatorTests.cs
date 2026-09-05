using IncidentCompass.Infrastructure.SourceContext;

namespace IncidentCompass.UnitTests;

public sealed class SourceContextOptionsValidatorTests
{
    [Fact]
    public void Validate_AcceptsBoundedAbsoluteMapping()
    {
        var options = ValidOptions();

        var result = new SourceContextOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0, 256, 262144, 21)]
    [InlineData(9, 256, 262144, 21)]
    [InlineData(8, 0, 262144, 21)]
    [InlineData(8, 256, 1024, 101)]
    public void Validate_RejectsOutOfRangeLimits(int frames, int candidates, int bytes, int lines)
    {
        var options = ValidOptions();
        options.MaxFrames = frames;
        options.MaxCandidateFiles = candidates;
        options.MaxSourceBytes = bytes;
        options.MaxExcerptLines = lines;

        var result = new SourceContextOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsDuplicateMappingAndNonAbsoluteBuildPrefix()
    {
        var options = ValidOptions();
        options.Roots = [options.Roots[0], options.Roots[0]];
        options.Roots[0].BuildPathPrefixes = ["relative/build"];

        var result = new SourceContextOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
    }

    private static SourceContextOptions ValidOptions() => new()
    {
        Roots =
        [
            new SourceContextRootOptions
            {
                ServiceName = "checkout",
                Release = "2026.08.28.1",
                RootPath = Path.GetPathRoot(Environment.CurrentDirectory)!,
                BuildPathPrefixes = [Path.GetPathRoot(Environment.CurrentDirectory)!]
            }
        ]
    };
}
