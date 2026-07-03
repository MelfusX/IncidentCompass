using IncidentCompass.Tester;

var options = TesterOptions.Parse(args, Environment.GetEnvironmentVariable("INCIDENTCOMPASS_TESTER_BASE_URL"));
using var client = new HttpClient
{
    BaseAddress = options.BaseUrl,
    Timeout = options.RequestTimeout
};

var tester = new DemoTester(client, options);
return await tester.RunAsync(CancellationToken.None);