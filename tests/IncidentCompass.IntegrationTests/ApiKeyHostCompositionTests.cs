using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IncidentCompass.IntegrationTests;

public sealed class ApiKeyHostCompositionTests
{
    private const string Key = "api_key_A_abcdefghijklmnopqrstuvwxyz123456";

    [Theory]
    [InlineData("IncidentCompass:ApiKeyAuth:PermitLimit", "0")]
    [InlineData("IncidentCompass:ApiKeyAuth:WindowSeconds", "0")]
    [InlineData("IncidentCompass:ApiKeyAuth:Credentials:0:Sha256Digest", "invalid")]
    [InlineData("IncidentCompass:ApiKeyAuth:Credentials:0:KeyId", "")]
    [InlineData("IncidentCompass:ApiKeyAuth:Credentials:0:TenantId", "")]
    public void EnabledHostRejectsInvalidOptionsWithoutSecretMaterial(string field, string value)
    {
        using var factory = ValidFactory().WithWebHostBuilder(builder => builder.UseSetting(field, value));

        var exception = Record.Exception(() => factory.CreateClient());
        var text = exception?.ToString() ?? string.Empty;

        Assert.NotNull(exception);
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Digest(Key), text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnabledHostRejectsDuplicateCredentialIdentityAndDigest()
    {
        using var factory = ValidFactory().WithWebHostBuilder(builder =>
        {
            SetCredential(builder, 1, "key-a", "tenant-b", Key);
        });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.DoesNotContain(Key, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Digest(Key), exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> ValidFactory() =>
        new MockProvidersWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:Enabled", "true");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:PermitLimit", "10");
            builder.UseSetting("IncidentCompass:ApiKeyAuth:WindowSeconds", "30");
            SetCredential(builder, 0, "key-a", "tenant-a", Key);
        });

    private static void SetCredential(IWebHostBuilder builder, int index, string keyId, string tenantId, string key)
    {
        var prefix = $"IncidentCompass:ApiKeyAuth:Credentials:{index}";
        builder.UseSetting($"{prefix}:KeyId", keyId);
        builder.UseSetting($"{prefix}:TenantId", tenantId);
        builder.UseSetting($"{prefix}:Sha256Digest", Digest(key));
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
}
