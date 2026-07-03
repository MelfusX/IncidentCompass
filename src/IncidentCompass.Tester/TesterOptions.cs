namespace IncidentCompass.Tester;

internal sealed record TesterOptions(
    Uri BaseUrl,
    TimeSpan RequestTimeout,
    TimeSpan PollTimeout,
    TimeSpan PollInterval)
{
    public static TesterOptions Parse(string[] args, string? environmentBaseUrl)
    {
        var baseUrl = environmentBaseUrl;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--base-url", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                baseUrl = args[index + 1];
                index++;
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:5198";
        }

        return new TesterOptions(
            new Uri(baseUrl.TrimEnd('/') + "/"),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(3),
            TimeSpan.FromSeconds(2));
    }
}