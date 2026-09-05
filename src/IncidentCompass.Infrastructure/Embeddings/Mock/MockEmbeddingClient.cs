using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Application.Core.Embeddings;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Infrastructure.Embeddings.Mock;

internal sealed class MockEmbeddingClient(IOptions<EmbeddingOptions> options) : IEmbeddingClient
{
    public Task<EmbeddingResponse> CreateEmbeddingAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        var dimensions = Math.Clamp(options.Value.MockDimensions, 1, 4096);
        var vector = CreateDeterministicVector(request.Input, dimensions);

        return Task.FromResult(new EmbeddingResponse(
            vector,
            request.Model,
            "mock",
            CountApproximateTokens(request.Input),
            request.CorrelationId));
    }

    private static float[] CreateDeterministicVector(string input, int dimensions)
    {
        var vector = new float[dimensions];
        foreach (var token in Tokenize(input))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var index = hash[0] % dimensions;
            var weight = 1f + hash[1] / 255f;
            vector[index] += weight;
        }

        return Normalize(vector);
    }

    private static IEnumerable<string> Tokenize(string input)
    {
        return input
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => token.Trim('`', '.', ',', ':', ';', '(', ')', '[', ']', '{', '}', '/', '\\'))
            .Where(static token => token.Length > 0)
            .Select(static token => token.ToUpperInvariant());
    }

    private static float[] Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(static value => value * value));
        if (magnitude <= 0)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / magnitude);
        }

        return vector;
    }

    private static int CountApproximateTokens(string value)
    {
        return value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }
}
