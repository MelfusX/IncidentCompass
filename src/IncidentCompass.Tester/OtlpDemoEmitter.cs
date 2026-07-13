using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace IncidentCompass.Tester;

internal static class OtlpDemoEmitter
{
    private const string SourceName = "IncidentCompass.Tester.OtlpDemo";

    public static OtlpEmission EmitFailure(Uri apiBaseUrl, string runId)
    {
        var serviceName = "otel-tester-" + runId;
        using var activitySource = new ActivitySource(SourceName, "1.0.0");
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(serviceName))
            .AddSource(SourceName)
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(apiBaseUrl, "v1/traces");
                options.Protocol = OtlpExportProtocol.HttpProtobuf;
            })
            .Build();
        using var activity = activitySource.StartActivity("POST /checkout", ActivityKind.Client)
            ?? throw new InvalidOperationException("The OpenTelemetry SDK did not create the demo activity.");

        activity.SetStatus(ActivityStatusCode.Error, "inventory timeout");
        activity.SetTag("exception.type", "TimeoutException");
        activity.SetTag("exception.message", "Checkout timed out while waiting on inventory.");
        activity.SetTag("http.request.method", "POST");
        activity.SetTag("http.route", "/checkout");
        activity.SetTag("http.response.status_code", 504);
        activity.SetTag("incidentcompass.event.id", "otel-sdk-" + runId);
        activity.Stop();

        if (!provider.ForceFlush(10_000))
        {
            throw new InvalidOperationException("The OTLP exporter did not confirm the demo trace export.");
        }

        return new OtlpEmission(serviceName, activity.TraceId.ToHexString(), activity.SpanId.ToHexString());
    }
}