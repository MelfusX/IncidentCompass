using System.Text;
using IncidentCompass.Application.SourceContext;
using IncidentCompass.Infrastructure.SourceContext;
using Microsoft.Extensions.Options;

namespace IncidentCompass.UnitTests;

public sealed class LocalSourceContextLookupTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("ic-source-").FullName;

    [Fact]
    public async Task Lookup_RootedAndBuildPrefixPathsReturnBoundedRepositoryExcerpt()
    {
        WriteSource("src/Checkout.cs", 40);
        var prefix = OperatingSystem.IsWindows() ? @"D:\agent\_work\repo" : "/agent/_work/repo";
        var lookup = CreateLookup(buildPrefixes: [prefix], excerptLines: 5);
        var direct = Path.Combine(root, "src", "Checkout.cs");
        var buildPath = prefix.TrimEnd('/', '\\') + Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar + "Checkout.cs";

        var result = await lookup.LookupAsync(
            new SourceLookupRequest("checkout", "r1", [new(direct, 20), new(buildPath, 30)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceLookupOutcome.Matched, result.Outcome);
        Assert.Equal(2, result.Matches.Count);
        Assert.All(result.Matches, match =>
        {
            Assert.Equal("src/Checkout.cs", match.RelativePath);
            Assert.Equal(5, match.LineEnd - match.LineStart + 1);
            Assert.Equal("r1", match.Release);
            Assert.Equal("heuristic", match.MappingMethod);
        });
    }

    [Fact]
    public async Task Lookup_RejectsTraversalForeignRootAndSiblingPrefix()
    {
        WriteSource("src/Checkout.cs", 10);
        var buildPrefix = OperatingSystem.IsWindows() ? @"D:\build\repo" : "/build/repo";
        var lookup = CreateLookup(buildPrefixes: [buildPrefix]);
        var sibling = root + "-sibling" + Path.DirectorySeparatorChar + "Checkout.cs";
        var foreign = Path.Combine(Path.GetPathRoot(root)!, "foreign", "Checkout.cs");
        var buildSibling = buildPrefix + "sibling" + Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar + "Checkout.cs";

        var result = await lookup.LookupAsync(
            new SourceLookupRequest("checkout", "r1", [
                new("../secret.cs", 1),
                new(sibling, 1),
                new(foreign, 1),
                new(buildSibling, 1)
            ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceLookupOutcome.NoMatch, result.Outcome);
        Assert.Equal(4, result.Limitations.Count(item => item.Code == "source_path_rejected"));
    }

    [Fact]
    public async Task Lookup_RejectsAmbiguousOversizedAndBinaryCandidates()
    {
        WriteSource("one/Duplicate.cs", 3);
        WriteSource("two/Duplicate.cs", 3);
        File.WriteAllText(Path.Combine(root, "Large.cs"), new string('a', 2048));
        File.WriteAllBytes(Path.Combine(root, "Binary.cs"), [1, 0, 2]);
        var lookup = CreateLookup(maxBytes: 1024);

        var result = await lookup.LookupAsync(
            new SourceLookupRequest("checkout", "r1", [
                new("Duplicate.cs", 1),
                new("Large.cs", 1),
                new("Binary.cs", 1)
            ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceLookupOutcome.NoMatch, result.Outcome);
        Assert.Contains(result.Limitations, item => item.Code == "source_path_ambiguous");
        Assert.Contains(result.Limitations, item => item.Code == "source_file_oversized");
        Assert.Contains(result.Limitations, item => item.Code == "source_file_binary");
    }

    [Fact]
    public async Task Lookup_MissingExactServiceReleaseMappingFailsClosed()
    {
        var result = await CreateLookup().LookupAsync(
            new SourceLookupRequest("checkout", "stale", [new("File.cs", 1)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceLookupOutcome.ConnectorUnavailable, result.Outcome);
        Assert.Equal("source_root_unavailable", result.Code);
    }

    [Fact]
    public async Task Lookup_EnforcesCandidateExtensionAndLineBounds()
    {
        WriteSource("one/First.cs", 3);
        WriteSource("two/Second.cs", 3);
        File.WriteAllText(Path.Combine(root, "notes.txt"), "text");
        var lookup = CreateLookup(maxCandidates: 1);

        var result = await lookup.LookupAsync(
            new SourceLookupRequest("checkout", "r1", [
                new("Missing.cs", 1),
                new("notes.txt", 1),
                new("one/First.cs", 99)
            ]),
            TestContext.Current.CancellationToken);

        Assert.Equal(SourceLookupOutcome.NoMatch, result.Outcome);
        Assert.Contains(result.Limitations, item => item.Code == "source_candidate_limit");
        Assert.Contains(result.Limitations, item => item.Code == "source_extension_rejected");
        Assert.Contains(result.Limitations, item => item.Code == "source_line_invalid");
    }

    [Fact]
    public async Task Lookup_CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateLookup().LookupAsync(
            new SourceLookupRequest("checkout", "r1", [new("File.cs", 1)]),
            cancellation.Token));
    }

    [Fact]
    public async Task Lookup_SymlinkEscapeCannotReturnContent()
    {
        var outside = Directory.CreateTempSubdirectory("ic-source-outside-");
        try
        {
            File.WriteAllText(Path.Combine(outside.FullName, "Secret.cs"), "secret");
            var link = Path.Combine(root, "linked");
            try
            {
                Directory.CreateSymbolicLink(link, outside.FullName);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException or UnauthorizedAccessException or IOException)
            {
                Assert.Skip("Symbolic links are not available to this test process.");
            }

            var result = await CreateLookup().LookupAsync(
                new SourceLookupRequest("checkout", "r1", [new("linked/Secret.cs", 1)]),
                TestContext.Current.CancellationToken);

            Assert.Equal(SourceLookupOutcome.NoMatch, result.Outcome);
            Assert.Contains(result.Limitations, item => item.Code == "source_path_rejected");
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private LocalSourceContextLookup CreateLookup(
        string[]? buildPrefixes = null,
        int excerptLines = 5,
        int maxBytes = 4096,
        int maxCandidates = 256)
    {
        return new LocalSourceContextLookup(Options.Create(new SourceContextOptions
        {
            MaxExcerptLines = excerptLines,
            MaxSourceBytes = maxBytes,
            MaxCandidateFiles = maxCandidates,
            Roots =
            [
                new SourceContextRootOptions
                {
                    ServiceName = "checkout",
                    Release = "r1",
                    RootPath = root,
                    BuildPathPrefixes = buildPrefixes ?? []
                }
            ]
        }));
    }

    private void WriteSource(string relativePath, int lineCount)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('\n', Enumerable.Range(1, lineCount).Select(index => $"line {index}")), Encoding.UTF8);
    }
}
