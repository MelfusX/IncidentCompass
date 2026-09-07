using IncidentCompass.Tester;

var options = TesterOptions.Parse(
    args,
    Environment.GetEnvironmentVariable("INCIDENTCOMPASS_TESTER_BASE_URL"),
    Environment.GetEnvironmentVariable("INCIDENTCOMPASS_TESTER_PUBLIC_BASE_URL"),
    Environment.GetEnvironmentVariable("INCIDENTCOMPASS_TESTER_REQUEST_TIMEOUT_SECONDS"),
    Environment.GetEnvironmentVariable("INCIDENTCOMPASS_TESTER_POLL_TIMEOUT_SECONDS"),
    Environment.GetEnvironmentVariable("INCIDENTCOMPASS_TESTER_POLL_INTERVAL_SECONDS"));
using var client = new HttpClient
{
    BaseAddress = options.BaseUrl,
    Timeout = options.RequestTimeout
};

var tester = new DemoTester(client, options);
return await tester.RunAsync(CancellationToken.None);
