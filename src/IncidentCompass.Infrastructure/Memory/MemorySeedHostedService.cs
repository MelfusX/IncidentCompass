using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Application.Core.Embeddings;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Memory;

internal sealed class MemorySeedHostedService(
    IOptions<MemorySeedOptions> options,
    IHostEnvironment environment,
    IServiceScopeFactory scopeFactory) : IHostedService
{
    private const string MemorySearchToolName = "memory_search";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var configurationRepository = scope.ServiceProvider.GetRequiredService<ITriageConfigurationRepository>();
        var embeddingClient = scope.ServiceProvider.GetRequiredService<IEmbeddingClient>();
        var memoryRepository = scope.ServiceProvider.GetRequiredService<IMemoryRepository>();
        var configuration = await configurationRepository.GetCurrentAsync(cancellationToken);
        var route = ResolveEmbeddingRoute(configuration);
        var rootDirectory = ResolveRootDirectory();
        foreach (var file in MemorySeedFileLoader.Load(rootDirectory))
        {
            await SeedFileAsync(file, route, embeddingClient, memoryRepository, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static TriageRouteSettings ResolveEmbeddingRoute(TriageConfiguration configuration)
    {
        if (!configuration.Tools.TryGetValue(MemorySearchToolName, out var tool) ||
            string.IsNullOrWhiteSpace(tool.EmbeddingRouteId))
        {
            throw new InvalidOperationException("memory_search must declare an EmbeddingRouteId before memory seeding can run.");
        }

        return configuration.Routes[tool.EmbeddingRouteId];
    }

    private async Task SeedFileAsync(
        MemorySeedFile file,
        TriageRouteSettings route,
        IEmbeddingClient embeddingClient,
        IMemoryRepository memoryRepository,
        CancellationToken cancellationToken)
    {
        var contentHash = ComputeSha256Hex(file.Content);
        var item = CreateItem(file, contentHash);
        if (await memoryRepository.SeedItemExistsAsync(item, cancellationToken))
        {
            return;
        }

        var embedding = await embeddingClient.CreateEmbeddingAsync(
            new EmbeddingRequest(file.Content, route.Model, "memory-seed:" + file.Source),
            cancellationToken);
        var chunk = new MemorySeedChunk(
            MemorySeedFileLoader.DeterministicId(item.Id + ":0:" + embedding.Provider + ":" + embedding.Model + ":" + embedding.Vector.Count),
            Position: 0,
            file.Content,
            ComputeSha256Hex(file.Content),
            embedding.Provider,
            embedding.Model,
            embedding.Vector.Count,
            embedding.Vector);

        await memoryRepository.UpsertSeedAsync(item, [chunk], cancellationToken);
    }

    private MemorySeedItem CreateItem(MemorySeedFile file, string contentHash)
    {
        return new MemorySeedItem(
            MemorySeedFileLoader.DeterministicId(options.Value.TenantId + ":" + file.Source + ":" + contentHash + ":1"),
            options.Value.TenantId,
            file.Kind,
            file.Source,
            file.Title,
            file.Content,
            contentHash,
            Version: 1,
            file.Tags);
    }

    private string ResolveRootDirectory()
    {
        var sourceDirectory = options.Value.SourceDirectory;
        return Path.IsPathRooted(sourceDirectory)
            ? sourceDirectory
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, sourceDirectory));
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
