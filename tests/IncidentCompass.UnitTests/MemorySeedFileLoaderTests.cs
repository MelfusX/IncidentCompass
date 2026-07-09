using IncidentCompass.Infrastructure.Memory;

namespace IncidentCompass.UnitTests;

public sealed class MemorySeedFileLoaderTests
{
    [Fact]
    public void Load_ParsesFrontmatterAndSupportsReleaseNotes()
    {
        var root = CreateRoot();
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "release-notes"));
            File.WriteAllText(Path.Combine(directory.FullName, "checkout-0.2.md"), """
                ---
                kind: ReleaseNote
                service: checkout-api
                component: payments
                release: 0.2
                tags: [checkout, migration]
                ---

                # Checkout 0.2

                The timeout fallback changed.
                """);

            var file = Assert.Single(MemorySeedFileLoader.Load(root));

            Assert.Equal("release_note", file.Kind);
            Assert.Equal("release-notes/checkout-0.2.md", file.Source);
            Assert.Equal("checkout-api", file.ServiceName);
            Assert.Equal("payments", file.Component);
            Assert.Equal("0.2", file.ReleaseName);
            Assert.Equal(["checkout", "migration"], file.Tags);
            Assert.DoesNotContain("kind:", file.Content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_UnknownFrontmatterKeyFailsInsteadOfSilentlyIgnoringTypo()
    {
        var root = CreateRoot();
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "runbooks"));
            File.WriteAllText(Path.Combine(directory.FullName, "broken.md"), """
                ---
                sevvice: checkout-api
                ---

                # Broken
                """);

            var exception = Assert.Throws<InvalidOperationException>(() => MemorySeedFileLoader.Load(root));

            Assert.Contains("unknown frontmatter key", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "incidentcompass-memory-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
