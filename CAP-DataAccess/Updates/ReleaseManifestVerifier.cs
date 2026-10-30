using CAP_Core.Update;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CAP_DataAccess.Updates;

/// <summary>Authenticates publisher metadata before an automatic update can be downloaded.</summary>
public sealed class ReleaseManifestVerifier
{
    private readonly IReadOnlyDictionary<string, string> _publicKeys;
    public const int MaximumEnvelopeBytes = 1024 * 1024;
    public const long MaximumArtifactBytes = 2L * 1024 * 1024 * 1024;

    public ReleaseManifestVerifier(IReadOnlyDictionary<string, string> publicKeys) =>
        _publicKeys = publicKeys.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Checks the signature, release identity, platform and selected asset.</summary>
    public VerifiedReleaseArtifact Verify(byte[] envelope, string releaseVersion,
        SemanticVersion currentVersion, string runtimeIdentifier, string assetName)
    {
        if (envelope.Length == 0 || envelope.Length > MaximumEnvelopeBytes)
            throw Invalid("The update manifest has an invalid length.");
        try
        {
            using var outer = JsonDocument.Parse(envelope, new JsonDocumentOptions { MaxDepth = 16 });
            var root = outer.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported manifest envelope.");
            Require(_publicKeys.TryGetValue(Text(root, "keyId"), out var publicKey), "Unknown publisher key.");
            var payload = Convert.FromBase64String(Text(root, "payload"));
            var signature = Convert.FromBase64String(Text(root, "signature"));
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(publicKey);
            Require(verifier.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7",
                "The publisher key must use ECDSA P-256.");
            Require(signature.Length == 64 && verifier.VerifyData(payload, signature,
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
                "The update manifest signature is invalid.");

            using var signed = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            return SelectAsset(signed.RootElement, releaseVersion, currentVersion, runtimeIdentifier, assetName);
        }
        catch (Exception error) when (error is JsonException or FormatException or CryptographicException
            or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("The update manifest could not be authenticated.", error);
        }
    }

    private static VerifiedReleaseArtifact SelectAsset(JsonElement signed, string releaseVersion,
        SemanticVersion currentVersion, string runtimeIdentifier, string assetName)
    {
        Require(signed.GetProperty("schemaVersion").GetInt32() == 1, "Unsupported signed manifest schema.");
        var version = Text(signed, "version");
        Require(Regex.IsMatch(version, @"\A(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\z"),
            "Invalid stable release version.");
        var parsed = SemanticVersion.Parse(version);
        var releaseIdentity = releaseVersion.StartsWith('v') ? releaseVersion[1..] : releaseVersion;
        Require(version == releaseIdentity && parsed > currentVersion,
            "The signed version does not identify a newer requested release.");
        var commit = Text(signed, "sourceCommit");
        Require(IsHex(commit, 40) || IsHex(commit, 64), "Invalid source commit identity.");
        var assets = signed.GetProperty("assets");
        Require(assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() is > 0 and <= 64,
            "Invalid signed asset inventory.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VerifiedReleaseArtifact? selected = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            Require(name is not ("." or "..") && !name.Any(c => c is '/' or '\\' or ':' || char.IsControl(c))
                && names.Add(name), "Invalid or duplicate artifact filename.");
            var rid = Text(asset, "runtimeIdentifier");
            Require(rid is "win-x64" or "win-arm64" or "linux-x64" or "linux-arm64" or "osx-x64" or "osx-arm64",
                "Unsupported artifact platform.");
            var size = asset.GetProperty("size").GetInt64();
            Require(size is > 0 and <= MaximumArtifactBytes, "Invalid artifact size.");
            var digest = Text(asset, "sha256");
            Require(IsHex(digest, 64), "Invalid artifact digest.");
            if (rid == runtimeIdentifier && name == assetName)
                selected = new VerifiedReleaseArtifact(version, commit, rid, name, size, digest.ToLowerInvariant());
        }
        return selected ?? throw Invalid("The requested artifact is not signed for this platform.");
    }

    private static bool IsHex(string text, int length) => text.Length == length && text.All(Uri.IsHexDigit);
    private static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw Invalid("Missing manifest field: " + name);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw Invalid(message);
    }
    private static InvalidDataException Invalid(string message) => new(message);
}

/// <summary>Artifact identity authenticated by a pinned publisher key.</summary>
public sealed class VerifiedReleaseArtifact
{
    internal VerifiedReleaseArtifact(string version, string commit, string runtimeIdentifier,
        string name, long size, string sha256)
    {
        Version = version;
        SourceCommit = commit;
        RuntimeIdentifier = runtimeIdentifier;
        Name = name;
        Size = size;
        Sha256 = sha256;
    }

    public string Version { get; }
    public string SourceCommit { get; }
    public string RuntimeIdentifier { get; }
    public string Name { get; }
    public long Size { get; }
    public string Sha256 { get; }
}
