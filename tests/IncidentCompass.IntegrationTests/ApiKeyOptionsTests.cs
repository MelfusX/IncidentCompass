using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Api.Configuration;
using IncidentCompass.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace IncidentCompass.IntegrationTests;

public sealed class ApiKeyOptionsTests
{
    private const string KeyA = "api_key_A_abcdefghijklmnopqrstuvwxyz123456";
    private const string KeyB = "api_key_B_abcdefghijklmnopqrstuvwxyz123456";

    [Fact]
    public void Validator_AcceptsDisabledDefaultsAndEnabledBoundedCredentials()
    {
        var validator = new ApiKeyAuthOptionsValidator();

        Assert.True(validator.Validate(null, new ApiKeyAuthOptions()).Succeeded);
        Assert.True(validator.Validate(null, OptionsFor(KeyA, "key-a", "tenant-a")).Succeeded);
    }

    [Theory]
    [InlineData("PermitLimit")]
    [InlineData("WindowSeconds")]
    [InlineData("Credentials")]
    [InlineData("DuplicateKeyId")]
    [InlineData("DuplicateDigest")]
    [InlineData("InvalidDigest")]
    public void Validator_RejectsUnsafeEnabledConfigurationWithoutEchoingDigest(string caseName)
    {
        const string digestSentinel = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var options = caseName switch
        {
            "PermitLimit" => OptionsFor(KeyA, "key-a", "tenant-a", permitLimit: 0),
            "WindowSeconds" => OptionsFor(KeyA, "key-a", "tenant-a", windowSeconds: 0),
            "Credentials" => new ApiKeyAuthOptions { Enabled = true },
            "DuplicateKeyId" => OptionsWithCredentials(
                new Credential("key-a", "tenant-a", Digest(KeyA)),
                new Credential("key-a", "tenant-b", digestSentinel)),
            "DuplicateDigest" => OptionsWithCredentials(
                new Credential("key-a", "tenant-a", digestSentinel),
                new Credential("key-b", "tenant-b", digestSentinel)),
            "InvalidDigest" => OptionsWithCredentials(new Credential("key-a", "tenant-a", "not-a-digest")),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };

        var result = new ApiKeyAuthOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.DoesNotContain(digestSentinel, string.Join(" ", result.Failures), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolver_ValidRotationRevokesOldKeyAndActivatesReplacement()
    {
        var (configuration, provider) = BuildConfiguration(KeyA, "key-a", "tenant-a");
        using var resolver = CreateResolver(configuration);

        AssertResolved(resolver, KeyA, "key-a", "tenant-a");
        SetCredential(provider, KeyB, "key-b", "tenant-b");
        configuration.Reload();

        Assert.False(resolver.TryResolve(Hash(KeyA), out _));
        AssertResolved(resolver, KeyB, "key-b", "tenant-b");
    }

    [Fact]
    public void Resolver_InvalidReloadDeniesAllAndLaterValidMapRecovers()
    {
        var (configuration, provider) = BuildConfiguration(KeyA, "key-a", "tenant-a");
        using var resolver = CreateResolver(configuration);

        provider.Set("IncidentCompass:ApiKeyAuth:Credentials:0:Sha256Digest", "invalid");
        configuration.Reload();
        Assert.True(resolver.AuthenticationRequired);
        Assert.False(resolver.TryResolve(Hash(KeyA), out _));

        SetCredential(provider, KeyB, "key-b", "tenant-b");
        configuration.Reload();
        AssertResolved(resolver, KeyB, "key-b", "tenant-b");
    }

    [Theory]
    [InlineData("IncidentCompass:ApiKeyAuth:Enabled", "false")]
    [InlineData("IncidentCompass:ApiKeyAuth:PermitLimit", "11")]
    [InlineData("IncidentCompass:ApiKeyAuth:WindowSeconds", "31")]
    public void Resolver_StaticFieldReloadDeniesAllUntilOriginalSettingsReturn(string field, string changedValue)
    {
        var (configuration, provider) = BuildConfiguration(KeyA, "key-a", "tenant-a");
        using var resolver = CreateResolver(configuration);

        provider.Set(field, changedValue);
        configuration.Reload();
        Assert.False(resolver.TryResolve(Hash(KeyA), out _));

        provider.Set("IncidentCompass:ApiKeyAuth:Enabled", "true");
        provider.Set("IncidentCompass:ApiKeyAuth:PermitLimit", "10");
        provider.Set("IncidentCompass:ApiKeyAuth:WindowSeconds", "30");
        configuration.Reload();
        AssertResolved(resolver, KeyA, "key-a", "tenant-a");
    }

    [Fact]
    public void Resolver_DisabledStartupRejectsLiveEnableAndRecoversOnlyToOriginalMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IncidentCompass:ApiKeyAuth:Enabled"] = "false",
                ["IncidentCompass:ApiKeyAuth:PermitLimit"] = "10",
                ["IncidentCompass:ApiKeyAuth:WindowSeconds"] = "30"
            })
            .Build();
        var provider = configuration.Providers.OfType<MemoryConfigurationProvider>().Single();
        using var resolver = new ApiKeyCredentialResolver(
            configuration,
            new ApiKeyAuthOptionsValidator(),
            new ApiKeyRuntimeSettings(false, 10, 30));

        Assert.False(resolver.AuthenticationRequired);
        provider.Set("IncidentCompass:ApiKeyAuth:Enabled", "true");
        SetCredential(provider, KeyA, "key-a", "tenant-a");
        configuration.Reload();
        Assert.True(resolver.AuthenticationRequired);
        Assert.False(resolver.TryResolve(Hash(KeyA), out _));

        provider.Set("IncidentCompass:ApiKeyAuth:Enabled", "false");
        configuration.Reload();
        Assert.False(resolver.AuthenticationRequired);
    }

    [Fact]
    public async Task Resolver_ConcurrentDecisionsObserveOnlyCompleteCredentialMaps()
    {
        var (configuration, provider) = BuildConfiguration(KeyA, "key-a", "tenant-a");
        using var resolver = CreateResolver(configuration);
        var unexpected = new List<string>();

        var readers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 2_000; iteration++)
            {
                CheckResolvedPair(resolver, KeyA, "key-a", "tenant-a", unexpected);
                CheckResolvedPair(resolver, KeyB, "key-b", "tenant-b", unexpected);
            }
        })).ToArray();

        for (var iteration = 0; iteration < 40; iteration++)
        {
            var useA = iteration % 2 == 0;
            SetCredential(provider, useA ? KeyA : KeyB, useA ? "key-a" : "key-b", useA ? "tenant-a" : "tenant-b");
            configuration.Reload();
        }

        await Task.WhenAll(readers);
        Assert.Empty(unexpected);
    }

    private static void CheckResolvedPair(
        ApiKeyCredentialResolver resolver,
        string key,
        string expectedKeyId,
        string expectedTenant,
        List<string> unexpected)
    {
        if (resolver.TryResolve(Hash(key), out var credential) &&
            (credential!.KeyId != expectedKeyId || credential.TenantId != expectedTenant))
        {
            lock (unexpected)
            {
                unexpected.Add("A credential decision combined fields from different snapshots.");
            }
        }
    }

    private static ApiKeyCredentialResolver CreateResolver(IConfigurationRoot configuration) =>
        new(
            configuration,
            new ApiKeyAuthOptionsValidator(),
            new ApiKeyRuntimeSettings(true, 10, 30));

    private static (IConfigurationRoot Configuration, MemoryConfigurationProvider Provider) BuildConfiguration(
        string key,
        string keyId,
        string tenantId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IncidentCompass:ApiKeyAuth:Enabled"] = "true",
                ["IncidentCompass:ApiKeyAuth:PermitLimit"] = "10",
                ["IncidentCompass:ApiKeyAuth:WindowSeconds"] = "30",
                ["IncidentCompass:ApiKeyAuth:Credentials:0:KeyId"] = keyId,
                ["IncidentCompass:ApiKeyAuth:Credentials:0:TenantId"] = tenantId,
                ["IncidentCompass:ApiKeyAuth:Credentials:0:Sha256Digest"] = Digest(key)
            })
            .Build();
        return (configuration, configuration.Providers.OfType<MemoryConfigurationProvider>().Single());
    }

    private static void SetCredential(MemoryConfigurationProvider provider, string key, string keyId, string tenantId)
    {
        provider.Set("IncidentCompass:ApiKeyAuth:Credentials:0:KeyId", keyId);
        provider.Set("IncidentCompass:ApiKeyAuth:Credentials:0:TenantId", tenantId);
        provider.Set("IncidentCompass:ApiKeyAuth:Credentials:0:Sha256Digest", Digest(key));
    }

    private static void AssertResolved(ApiKeyCredentialResolver resolver, string key, string keyId, string tenantId)
    {
        Assert.True(resolver.TryResolve(Hash(key), out var credential));
        Assert.Equal(keyId, credential!.KeyId);
        Assert.Equal(tenantId, credential.TenantId);
    }

    private static ApiKeyAuthOptions OptionsFor(
        string key,
        string keyId,
        string tenantId,
        int permitLimit = 10,
        int windowSeconds = 30) =>
        new()
        {
            Enabled = true,
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            Credentials = [new ApiKeyCredentialOptions { KeyId = keyId, TenantId = tenantId, Sha256Digest = Digest(key) }]
        };

    private static ApiKeyAuthOptions OptionsWithCredentials(params Credential[] credentials) =>
        new()
        {
            Enabled = true,
            Credentials = credentials.Select(static credential => new ApiKeyCredentialOptions
            {
                KeyId = credential.KeyId,
                TenantId = credential.TenantId,
                Sha256Digest = credential.Digest
            }).ToArray()
        };

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.ASCII.GetBytes(key));

    private static string Digest(string key) => Convert.ToHexString(Hash(key));

    private sealed record Credential(string KeyId, string TenantId, string Digest);
}
