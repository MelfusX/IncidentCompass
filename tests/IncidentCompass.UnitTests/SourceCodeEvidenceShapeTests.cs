using System.Text.Json;
using IncidentCompass.Application.Investigation.Reports.Context;

namespace IncidentCompass.UnitTests;

public sealed class SourceCodeEvidenceShapeTests
{
    [Fact]
    public void IsCitable_RequiresClosedSourceShapeAndMatchingRelease()
    {
        var payload = JsonSerializer.Serialize(new
        {
            evidenceKind = "SourceCode",
            relativePath = "src/Checkout.cs",
            lineStart = 10,
            lineEnd = 12,
            excerpt = "line 10\nline 11\nline 12",
            release = "r1",
            mappingMethod = "heuristic"
        });

        Assert.True(SourceCodeEvidenceShape.IsCitable("source:r1:src/Checkout.cs", payload, "r1"));
        Assert.False(SourceCodeEvidenceShape.IsCitable("source:r1:src/Checkout.cs", payload, "r2"));
        Assert.False(SourceCodeEvidenceShape.IsCitable("source:r1:src/Other.cs", payload, "r1"));
        Assert.False(SourceCodeEvidenceShape.IsCitable("other:src/Checkout.cs", payload, "r1"));
    }

    [Fact]
    public void IsCitable_RejectsAdditionalPayloadProperties()
    {
        var payload = JsonSerializer.Serialize(new
        {
            evidenceKind = "SourceCode",
            relativePath = "src/Checkout.cs",
            lineStart = 10,
            lineEnd = 12,
            excerpt = "bounded",
            release = "r1",
            mappingMethod = "heuristic",
            absolutePath = "C:/private/Checkout.cs"
        });

        Assert.False(SourceCodeEvidenceShape.IsCitable("source:r1:src/Checkout.cs", payload, "r1"));
    }

    [Theory]
    [InlineData("Unknown", "src/Checkout.cs", 10, 12, "heuristic")]
    [InlineData("SourceCode", "../Checkout.cs", 10, 12, "heuristic")]
    [InlineData("SourceCode", "src/Checkout.cs", 0, 12, "heuristic")]
    [InlineData("SourceCode", "src/Checkout.cs", 12, 10, "heuristic")]
    [InlineData("SourceCode", "src/Checkout.cs", 10, 12, "source-link")]
    public void IsCitable_RejectsInvalidDiscriminatorAndShape(
        string evidenceKind,
        string relativePath,
        int lineStart,
        int lineEnd,
        string mappingMethod)
    {
        var payload = JsonSerializer.Serialize(new
        {
            evidenceKind,
            relativePath,
            lineStart,
            lineEnd,
            excerpt = "bounded",
            release = "r1",
            mappingMethod
        });

        Assert.False(SourceCodeEvidenceShape.IsCitable("source:r1:path", payload, "r1"));
    }
}
