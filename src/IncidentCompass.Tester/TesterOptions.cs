namespace IncidentCompass.Tester;

internal sealed record TesterOptions(
    Uri BaseUrl,
    Uri PublicBaseUrl,
    TimeSpan RequestTimeout,
    TimeSpan PollTimeout,
    TimeSpan PollInterval)
{
    public static TesterOptions Parse(string[] args, string? environmentBaseUrl, string? environmentPublicBaseUrl)
    {
        var baseUrl = environmentBaseUrl;
        var publicBaseUrl = environmentPublicBaseUrl;
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
            TimeSpan.FromSeconds(2));
    }

    private static Uri ToBaseUri(string value) => new(value.TrimEnd('/') + "/");
}
