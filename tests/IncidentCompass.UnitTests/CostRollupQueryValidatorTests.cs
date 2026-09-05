using IncidentCompass.Application.Observability.CostRollup;

namespace IncidentCompass.UnitTests;

public sealed class CostRollupQueryValidatorTests
{
    private readonly CostRollupQueryValidator validator = new();

    [Fact]
    public void ExactThirtyOneDayUtcWindowIsValid()
    {
        var fromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var result = validator.Validate(new CostRollupQuery(fromUtc, fromUtc.AddDays(31)));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00+01:00", "2026-01-01T01:00:00Z")]
    [InlineData("2026-01-01T00:00:00Z", "2026-01-01T01:00:00-01:00")]
    public void NonUtcBoundaryIsRejected(string from, string to)
    {
        var result = validator.Validate(new CostRollupQuery(
            DateTimeOffset.Parse(from),
            DateTimeOffset.Parse(to)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName is "FromUtc" or "ToUtc");
    }

    [Theory]
    [InlineData("2026-01-01T01:00:00Z", "2026-01-01T01:00:00Z")]
    [InlineData("2026-01-01T02:00:00Z", "2026-01-01T01:00:00Z")]
    public void EmptyOrReversedWindowIsRejected(string from, string to)
    {
        var result = validator.Validate(new CostRollupQuery(
            DateTimeOffset.Parse(from),
            DateTimeOffset.Parse(to)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "window");
    }

    [Fact]
    public void WindowOverThirtyOneDaysIsRejected()
    {
        var fromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var result = validator.Validate(new CostRollupQuery(
            fromUtc,
            fromUtc.AddDays(31).AddTicks(1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "window");
    }
}
