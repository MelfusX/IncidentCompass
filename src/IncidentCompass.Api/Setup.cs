using IncidentCompass.Api.Health;
using IncidentCompass.Api.Configuration;
using IncidentCompass.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;

namespace IncidentCompass.Api;

public static class Setup
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<OtlpPayloadReader>();
        AddApiKeyBoundary(services, configuration);
        services.AddApiUserContext(configuration, environment);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck<PostgresReadinessHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<MemorySeedSyncHealthCheck>("memory_seed_sync", tags: ["ready"]);
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<ApiKeyOpenApiDocumentTransformer>();
            options.AddOperationTransformer<ApiKeyOpenApiOperationTransformer>();
        });

        return services;
    }

    private static void AddApiKeyBoundary(IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration
            .GetSection(ApiKeyAuthOptions.SectionName)
            .Get<ApiKeyAuthOptions>() ?? new ApiKeyAuthOptions();
        var runtimeSettings = new ApiKeyRuntimeSettings(
            configured.Enabled,
            Math.Clamp(configured.PermitLimit, 1, 10_000),
            Math.Clamp(configured.WindowSeconds, 1, 3_600));

        services.AddSingleton(runtimeSettings);
        services.AddSingleton<ApiKeyAuthOptionsValidator>();
        services.AddOptions<ApiKeyAuthOptions>()
            .Bind(configuration.GetSection(ApiKeyAuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApiKeyAuthOptions>, ApiKeyAuthOptionsValidator>();
        services.AddSingleton<ApiKeyCredentialResolver>();
        services.AddSingleton<ApiAuthenticationMetrics>();

        services.AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                static _ => { });
        services.AddSingleton<IAuthorizationHandler, ApiKeyAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .AddRequirements(new ApiKeyAuthorizationRequirement())
                .Build();
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var keyId = context.User.FindFirst(ApiKeyAuthenticationDefaults.KeyIdClaim)?.Value;
                if (!runtimeSettings.Enabled || string.IsNullOrEmpty(keyId))
                {
                    return RateLimitPartition.GetNoLimiter("anonymous");
                }

                return RateLimitPartition.GetFixedWindowLimiter(keyId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = runtimeSettings.PermitLimit,
                    Window = TimeSpan.FromSeconds(runtimeSettings.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });
    }
}
