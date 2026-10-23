using System.Security.Cryptography;
using System.Text;

namespace CAP_Core.Export;

/// <summary>Stable identity of the renderer, resolved interpreter and loaded PDK revision.</summary>
public static class PreviewEnvironmentIdentity
{
    public static string Create(string python, string script, string? sourceRevision) =>
        Hash($"{FileRevision(python)}\n{FileRevision(script)}\n{sourceRevision}");

    public static string FileRevision(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}" : path;
        }
        catch { return path; }
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
