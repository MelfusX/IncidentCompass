using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace IncidentCompass.Infrastructure.Postgres;

internal sealed record PostgresSchemaMigration(
    int Version,
    string Name,
    IReadOnlyList<string> ScriptNames)
{
    public async Task<string> ComputeChecksumAsync(CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var scriptName in ScriptNames)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(scriptName));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(
                await PostgresMigrationScriptExecutor.ReadAsync(scriptName, cancellationToken)));
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        foreach (var scriptName in ScriptNames)
        {
            await PostgresMigrationScriptExecutor.ExecuteAsync(
                connection,
                transaction,
                scriptName,
                cancellationToken);
        }
    }
}
