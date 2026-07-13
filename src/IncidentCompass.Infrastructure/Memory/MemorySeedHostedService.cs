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
        var scan = MemorySeedFileLoader.LoadScan(ResolveRootDirectory());
        var entries = new List<MemorySeedEntry>(scan.Files.Count);
        foreach (var file in scan.Files)
        {
            entries.Add(await PrepareSeedAsync(
                file, route, embeddingClient, memoryRepository, cancellationToken));
        }

        await memoryRepository.ReconcileSeedCorpusAsync(
            new MemorySeedCorpus(
                options.Value.TenantId, options.Value.Owner, Guid.NewGuid(),
                scan.PresentDirectories, entries),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static TriageRouteSettings ResolveEmbeddingRoute(TriageConfiguration configuration)
    {
        if (!configuration.Tools.TryGetValue(MemorySearchToolName, out var tool) ||
            string.IsNullOrWhiteSpace(tool.EmbeddingRouteId))
        {
            throw new InvalidOperationException("memory_search must declare an EmbeddingRouteId before memory seeding can run.");
        }

        return configuration.Routes[tool.EmbeddingRouteId];
    }

    private async Task<MemorySeedEntry> PrepareSeedAsync(
        MemorySeedFile file,
        TriageRouteSettings route,
        IEmbeddingClient embeddingClient,
        IMemoryRepository memoryRepository,
        CancellationToken cancellationToken)
    {
        var contentHash = ComputeSha256Hex(file.Content);
        var item = CreateItem(file, contentHash);
        if (await memoryRepository.SeedItemExistsAsync(options.Value.Owner, item, cancellationToken))
        {
            return new MemorySeedEntry(item, []);
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

        return new MemorySeedEntry(item, [chunk]);
    }

    private MemorySeedItem CreateItem(MemorySeedFile file, string contentHash)
    {
        return new MemorySeedItem(
            MemorySeedFileLoader.DeterministicId(
                options.Value.TenantId + ":" + options.Value.Owner + ":" + file.Source + ":seed-v3"),
            options.Value.TenantId,
            file.Kind,
            file.Source,
            file.Title,
            file.Content,
            contentHash,
            Version: 1,
            file.Tags,
            file.ServiceName,
            file.Component,
            file.ReleaseName);
    }

    private string ResolveRootDirectory()
    {
        var sourceDirectory = options.Value.SourceDirectory;
        return Path.IsPathRooted(sourceDirectory)
            ? sourceDirectory
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, sourceDirectory));
    }

    private static string ComputeSha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}