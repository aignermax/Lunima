using System.Security.Cryptography;
using System.Text;

namespace CAP_Core.Export;

/// <summary>Stable digest of a preview environment's already-resolved identity values.</summary>
public static class PreviewIdentityHash
{
    public static string Compute(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
