using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace IncidentCompass.Application.Governance.Tools;

public static class ExternalActionBinding
{
    private const string Domain = "IncidentCompass.ExternalActionBinding.v1";

    public static string ComputeFingerprint(
        string toolKind,
        string logicalTargetId,
        string normalizedEndpointAuthority,
        string resourceId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Domain);
        Append(hash, toolKind);
        Append(hash, logicalTargetId);
        Append(hash, normalizedEndpointAuthority);
        Append(hash, resourceId);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
