using System.Net;
using System.Net.Http.Json;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Trace.V1;
using IncidentCompass.Application.Core.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IncidentCompass.IntegrationTests;

public sealed class ApiV1EndpointTests(MockProvidersWebApplicationFactory factory)
    : IClassFixture<MockProvidersWebApplicationFactory>
{
    [Fact]
    public async Task Health_ReturnsHealthyStatusUnderApiV1()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal("api", body.Component);
        Assert.Equal("v1", body.ApiVersion);
    }

    [Fact]
    public async Task OpenApi_DevelopmentPublishesDocument()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        using var client = developmentFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"/api/v1/health\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CurrentUser_UsesDemoHeaders()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Add("X-Demo-User-Id", "alice");
        request.Headers.Add("X-Demo-Tenant-Id", "local");
        request.Headers.Add("X-Demo-Roles", "developer,admin");
        request.Headers.Add("X-Demo-Groups", "demo,engineering");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsAuthenticated);
        Assert.Equal("alice", body.UserId);
        Assert.Equal("local", body.TenantId);
        Assert.Contains("admin", body.Roles);
        Assert.Contains("engineering", body.Groups);
    }

    [Fact]
    public async Task CurrentUser_DeduplicatesDemoRoleAndGroupHeaders()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Add("X-Demo-User-Id", "alice");
        request.Headers.Add("X-Demo-Tenant-Id", "local");
        request.Headers.Add("X-Demo-Roles", ["developer,developer", "Developer,admin"]);
        request.Headers.Add("X-Demo-Groups", ["demo,demo", "Demo,engineering"]);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(body);
        Assert.Equal(["developer", "admin"], body.Roles);
        Assert.Equal(["demo", "engineering"], body.Groups);
    }

    [Fact]
    public async Task DemoAuth_DevelopmentWithoutHeadersUsesDemoDefaults()
    {
        using var developmentFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        using var client = developmentFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsAuthenticated);
        Assert.Equal("demo-user", body.UserId);
        Assert.Equal("demo-tenant", body.TenantId);
        Assert.Contains("developer", body.Roles);
        Assert.Contains("demo", body.Groups);
    }

    [Fact]
    public void ApiComposition_ResolvesOnlyForegroundUserContext()
    {
        using var scope = factory.Services.CreateScope();

        var contexts = scope.ServiceProvider.GetServices<IUserContext>().ToArray();

        var context = Assert.Single(contexts);
        Assert.Equal("DemoHeaderUserContext", context.GetType().Name);
    }

    [Fact]
    public void DemoAuth_ProductionWithoutExplicitOptInFailsFast()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("IncidentCompass:DemoAuth:AllowInNonDevelopment", "false");
        });

        var exception = Record.Exception(() =>
            productionFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            }));

        Assert.NotNull(exception);
        Assert.Contains("Demo header auth", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IncidentCompass:DemoAuth:Enabled", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IncidentCompass:DemoAuth:AllowInNonDevelopment", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("incidentcompass_dev_password", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoAuth_ProductionWithExplicitOptInFailsFast()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("IncidentCompass:DemoAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:DemoAuth:AllowInNonDevelopment", "true");
        });

        var exception = Record.Exception(() =>
            productionFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            }));

        Assert.NotNull(exception);
        Assert.Contains("real IUserContext", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IncidentCompass:DemoAuth:Enabled", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IncidentCompass:DemoAuth:AllowInNonDevelopment", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiComposition_ProductionUsesRegisteredForegroundUserContext()
    {
        using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IUserContext, TestForegroundUserContext>();
            });
        });
        using var client = productionFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsAuthenticated);
        Assert.Equal("real-user", body.UserId);
        Assert.Equal("real-tenant", body.TenantId);
        Assert.Contains("operator", body.Roles);
    }

    [Fact]
    public async Task DemoAuth_NonProductionWithExplicitOptInWithoutUserHeaderIsAnonymous()
    {
        using var demoFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("IncidentCompass:DemoAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:DemoAuth:AllowInNonDevelopment", "true");
        });
        using var client = demoFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Add("X-Demo-Roles", "developer,admin");
        request.Headers.Add("X-Demo-Groups", "demo,engineering");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(body);
        Assert.False(body.IsAuthenticated);
        Assert.Null(body.UserId);
        Assert.Null(body.TenantId);
        Assert.Empty(body.Roles);
        Assert.Empty(body.Groups);
    }

    [Fact]
    public async Task DemoAuth_NonProductionWithExplicitOptInUsesDemoHeaders()
    {
        using var demoFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("IncidentCompass:DemoAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:DemoAuth:AllowInNonDevelopment", "true");
        });
        using var client = demoFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Add("X-Demo-User-Id", "alice");
        request.Headers.Add("X-Demo-Tenant-Id", "local");
        request.Headers.Add("X-Demo-Roles", "developer,admin");
        request.Headers.Add("X-Demo-Groups", "demo,engineering");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(body);
        Assert.True(body.IsAuthenticated);
        Assert.Equal("alice", body.UserId);
        Assert.Equal("local", body.TenantId);
        Assert.Contains("admin", body.Roles);
        Assert.Contains("engineering", body.Groups);
    }

    [Fact]
    public async Task MemorySyncHealth_ReturnsMetadataOnlyStatus()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/v1/health/memory-sync");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MemorySyncHealthResponse>();
        Assert.NotNull(body);
        Assert.False(body.Enabled);
        Assert.False(body.RuntimeResyncEnabled);
        Assert.Null(body.LastErrorCode);
    }

    [Fact]
    public async Task OtlpTraceEndpoint_AcceptsAnEmptyProtobufExport()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var content = new ByteArrayContent([]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        var response = await client.PostAsync("/v1/traces", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OtlpTraceEndpoint_RejectsChunkedPayloadOverTheConfiguredLimit()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var content = new ChunkedByteContent(new byte[65537]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        var response = await client.PostAsync("/v1/traces", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task OtlpTraceEndpoint_RejectsUnsupportedMediaType()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var content = new ByteArrayContent([]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync("/v1/traces", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task OtlpTraceEndpoint_RejectsMalformedProtobuf()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var content = new ByteArrayContent([0xff]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");

        var response = await client.PostAsync("/v1/traces", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OtlpTraceEndpoint_ValidatesTraceAndSpanIdentifiers()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var validTrace = ByteString.CopyFrom(new byte[] { 0, 1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 });
        var validSpan = ByteString.CopyFrom(new byte[] { 0, 2, 3, 4, 5, 6, 7, 8 });
        var invalid = new[]
        {
            (ByteString.Empty, validSpan, ByteString.Empty),
            (ByteString.CopyFrom(new byte[16]), validSpan, ByteString.Empty),
            (ByteString.CopyFrom(new byte[15]), validSpan, ByteString.Empty),
            (validTrace, ByteString.Empty, ByteString.Empty),
            (validTrace, ByteString.CopyFrom(new byte[8]), ByteString.Empty),
            (validTrace, ByteString.CopyFrom(new byte[7]), ByteString.Empty),
            (validTrace, validSpan, ByteString.CopyFrom(new byte[8])),
            (validTrace, validSpan, ByteString.CopyFrom(new byte[7]))
        };

        foreach (var (traceId, spanId, parentSpanId) in invalid)
        {
            var export = CreateTraceExport("invalid", traceId, spanId, parentSpanId);
            using var content = new ByteArrayContent(export.ToByteArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
            var response = await client.PostAsync("/v1/traces", content, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var validExport = CreateTraceExport("valid", validTrace, validSpan, ByteString.Empty);
        using var validContent = new ByteArrayContent(validExport.ToByteArray());
        validContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        var validResponse = await client.PostAsync("/v1/traces", validContent, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
    }

    private static ExportTraceServiceRequest CreateTraceExport(
        string name,
        ByteString traceId,
        ByteString spanId,
        ByteString parentSpanId) =>
        new()
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                new Span { Name = name, TraceId = traceId, SpanId = spanId, ParentSpanId = parentSpanId }
                            }
                        }
                    }
                }
            }
        };

    private sealed class ChunkedByteContent(byte[] payload) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            const int chunkSize = 4096;
            for (var offset = 0; offset < payload.Length; offset += chunkSize)
            {
                var length = Math.Min(chunkSize, payload.Length - offset);
                await stream.WriteAsync(payload.AsMemory(offset, length));
            }
        }
    }

    private sealed record MemorySyncHealthResponse(
        bool Enabled,
        bool RuntimeResyncEnabled,
        DateTimeOffset? LastAttemptAtUtc,
        DateTimeOffset? LastSuccessAtUtc,
        Guid? ActiveGeneration,
        string? LastErrorCode);

    private sealed record HealthResponse(
        string Status,
        string Component,
        string ApiVersion,
        DateTimeOffset CheckedAtUtc);

    private sealed record CurrentUserResponse(
        bool IsAuthenticated,
        string? UserId,
        string? TenantId,
        IReadOnlyCollection<string> Roles,
        IReadOnlyCollection<string> Groups);

    private sealed class TestForegroundUserContext : IUserContext
    {
        public bool IsAuthenticated => true;

        public string? UserId => "real-user";

        public string? TenantId => "real-tenant";

        public IReadOnlyCollection<string> Roles { get; } = ["operator"];

        public IReadOnlyCollection<string> Groups { get; } = ["foreground"];
    }
}
