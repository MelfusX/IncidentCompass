using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace IncidentCompass.Api.Security;

internal sealed class ApiKeyOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationDefaults.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationDefaults.HeaderName,
            Description = "Host-issued IncidentCompass API key."
        };
        return Task.CompletedTask;
    }
}
