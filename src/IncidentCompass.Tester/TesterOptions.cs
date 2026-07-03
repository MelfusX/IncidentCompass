namespace IncidentCompass.Tester;

internal sealed record TesterOptions(
    Uri BaseUrl,
    Uri PublicBaseUrl,
    TimeSpan RequestTimeout,
    TimeSpan PollTimeout,
    TimeSpan PollInterval,
    TimeSpan ScenarioTimeout,
    TimeSpan TotalTimeout)
{
    public static TesterOptions Parse(string[] args, string? environmentBaseUrl, string? environmentPublicBaseUrl)
    {
        var baseUrl = environmentBaseUrl;
        var publicBaseUrl = environmentPublicBaseUrl;
        var scenarioTimeout = TimeSpan.FromMinutes(4);
        var totalTimeout = TimeSpan.FromMinutes(15);
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--base-url", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                baseUrl = args[index + 1];
                index++;
                continue;
            }

            if (string.Equals(args[index], "--public-base-url", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                publicBaseUrl = args[index + 1];
                index++;
                continue;
            }

            if (string.Equals(args[index], "--scenario-timeout-seconds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                scenarioTimeout = ReadPositiveSeconds(args[index + 1], "--scenario-timeout-seconds");
                index++;
                continue;
            }

            if (string.Equals(args[index], "--total-timeout-seconds", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                totalTimeout = ReadPositiveSeconds(args[index + 1], "--total-timeout-seconds");
                index++;
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:5198";
        }

        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            publicBaseUrl = baseUrl;
        }

        return new TesterOptions(
            ToBaseUri(baseUrl),
            ToBaseUri(publicBaseUrl),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(2),
            scenarioTimeout,
            totalTimeout);
    }

    private static TimeSpan ReadPositiveSeconds(string value, string optionName)
    {
        if (!int.TryParse(value, out var seconds) || seconds <= 0)
        {
            throw new ArgumentException(optionName + " must be a positive integer number of seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static Uri ToBaseUri(string value) => new(value.TrimEnd('/') + "/");
}
