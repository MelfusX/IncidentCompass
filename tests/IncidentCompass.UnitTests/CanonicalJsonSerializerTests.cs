using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;

namespace IncidentCompass.UnitTests;

public sealed class CanonicalJsonSerializerTests
{
    [Fact]
    public void Canonicalize_ObjectKeyOrderDoesNotAffectResult()
    {
        var first = JsonNode.Parse("""{"b":1,"a":2}""");
        var second = JsonNode.Parse("""{"a":2,"b":1}""");

        Assert.Equal(CanonicalJsonSerializer.Canonicalize(first), CanonicalJsonSerializer.Canonicalize(second));
    }

    [Fact]
    public void Canonicalize_SortsNestedObjectKeysToo()
    {
        var first = JsonNode.Parse("""{"outer":{"z":1,"a":2}}""");
        var second = JsonNode.Parse("""{"outer":{"a":2,"z":1}}""");

        Assert.Equal(CanonicalJsonSerializer.Canonicalize(first), CanonicalJsonSerializer.Canonicalize(second));
    }

    [Fact]
    public void Canonicalize_PreservesArrayElementOrder()
    {
        var node = JsonNode.Parse("""{"items":[3,1,2]}""");

        var canonical = CanonicalJsonSerializer.Canonicalize(node);

        Assert.Contains("\"items\":[3,1,2]", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeSha256Hex_TwoArgument_ChangesWhenEitherArgumentChanges()
    {
        var baseline = CanonicalJsonSerializer.ComputeSha256Hex("a", "b");
        var changedFirst = CanonicalJsonSerializer.ComputeSha256Hex("x", "b");
        var changedSecond = CanonicalJsonSerializer.ComputeSha256Hex("a", "x");

        Assert.NotEqual(baseline, changedFirst);
        Assert.NotEqual(baseline, changedSecond);
        Assert.NotEqual(changedFirst, changedSecond);
    }

    [Fact]
    public void ComputeSha256Hex_IsDeterministic()
    {
        var first = CanonicalJsonSerializer.ComputeSha256Hex("same", "input");
        var second = CanonicalJsonSerializer.ComputeSha256Hex("same", "input");

        Assert.Equal(first, second);
    }
}
