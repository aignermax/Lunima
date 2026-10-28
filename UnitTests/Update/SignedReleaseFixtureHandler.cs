using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CAP_DataAccess.Updates;

namespace UnitTests.Update;

/// <summary>Adds genuine signed metadata to existing update-flow HTTP fixtures.</summary>
internal sealed class SignedReleaseFixtureHandler : DelegatingHandler
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private byte[]? _manifest;
    internal static readonly byte[] ArtifactBytes = [1, 2, 3, 4];
    internal ReleaseManifestVerifier Verifier { get; }

    internal SignedReleaseFixtureHandler(HttpMessageHandler inner) : base(inner) =>
        Verifier = new ReleaseManifestVerifier(new Dictionary<string, string>
        {
            ["fixture"] = _key.ExportSubjectPublicKeyInfoPem()
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/" + ReleaseManifestClient.AssetName, StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_manifest ?? throw new InvalidOperationException("Check release metadata first."))
            };
        var response = await base.SendAsync(request, cancellationToken);
        if (request.RequestUri.Host != "api.github.com" || !response.IsSuccessStatusCode) return response;
        var release = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        var assets = release["assets"]!.AsArray();
        var rows = assets.Select(asset => new
        {
            name = asset!["name"]!.GetValue<string>(),
            runtimeIdentifier = PlatformFor(asset["name"]!.GetValue<string>()),
            size = ArtifactBytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(ArtifactBytes))
        }).ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, version = release["tag_name"]!.GetValue<string>().TrimStart('v'),
            sourceCommit = new string('a', 40), assets = rows
        });
        _manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, keyId = "fixture", payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(_key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        });
        foreach (var asset in assets) asset!["size"] = ArtifactBytes.Length;
        assets.Add(new JsonObject
        {
            ["name"] = ReleaseManifestClient.AssetName,
            ["browser_download_url"] = "https://signed-fixture.test/" + ReleaseManifestClient.AssetName,
            ["size"] = _manifest.Length, ["content_type"] = "application/json"
        });
        response.Content.Dispose();
        response.Content = new StringContent(release.ToJsonString());
        return response;
    }

    private static string PlatformFor(string name)
    {
        if (name.EndsWith(".msi", StringComparison.Ordinal)) return "win-x64";
        if (name.Contains("osx-arm64", StringComparison.Ordinal)) return "osx-arm64";
        if (name.Contains("osx-x64", StringComparison.Ordinal)) return "osx-x64";
        var architecture = RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        return (name.EndsWith(".dmg", StringComparison.Ordinal) ? "osx-" : "linux-") + architecture;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _key.Dispose();
        base.Dispose(disposing);
    }
}
