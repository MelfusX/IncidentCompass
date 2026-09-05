using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.Application.Governance.ActionApprovals;

public static class ActionApprovalContractV1
{
    private const string ApprovalDomain = "IncidentCompass.ActionApproval.v1";
    private const string ProvenanceDomain = "IncidentCompass.ActionProvenance.v1";

    public static string ComputePayloadSha256(ReadOnlySpan<byte> payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload));

    public static string ComputeProvenanceSha256(
        Guid originReportId,
        IEnumerable<ActionProvenanceIdentity> evidence)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, ProvenanceDomain);
        Append(hash, originReportId.ToString("D"));
        foreach (var item in evidence
                     .OrderBy(static item => item.ArtifactId)
                     .ThenBy(static item => item.TrustClass))
        {
            Append(hash, item.ArtifactId.ToString("D"));
            Append(hash, item.TrustClass.ToStorageValue());
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string ComputeApprovalSha256(
        Guid originReportId,
        string toolId,
        ActionCategory category,
        ActionExecutionMode mode,
        string logicalTargetId,
        string adapterBindingFingerprint,
        string payloadSha256,
        ReadOnlySpan<byte> canonicalPayload,
        string provenanceSha256)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, ApprovalDomain);
        Append(hash, originReportId.ToString("D"));
        Append(hash, toolId);
        Append(hash, category.ToStorageValue());
        Append(hash, mode.ToStorageValue());
        Append(hash, logicalTargetId);
        Append(hash, adapterBindingFingerprint);
        Append(hash, payloadSha256);
        Append(hash, canonicalPayload);
        Append(hash, provenanceSha256);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value) => Append(hash, Encoding.UTF8.GetBytes(value));

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
