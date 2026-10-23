using System.Security.Cryptography;
using System.Text.Json;
using CAP_Core.Update;
using CAP_DataAccess.Updates;
using Shouldly;

namespace UnitTests.Update;

public sealed class ReleaseManifestVerifierTests : IDisposable
{
    private readonly ECDsa _publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private const string AssetName = "Lunima-Setup-2.0.0.msi";
    private const string Commit = "0123456789012345678901234567890123456789";
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ValidPublisherSignature_BindsVersionCommitPlatformAndBytes()
    {
        var result = Verify(Envelope(Payload()));
        result.Version.ShouldBe("2.0.0");
        result.SourceCommit.ShouldBe(Commit);
        result.RuntimeIdentifier.ShouldBe("win-x64");
        result.Name.ShouldBe(AssetName);
        result.Size.ShouldBe(42);
        result.Sha256.ShouldBe(Digest);
    }

    [Fact]
    public void ChangedPayload_IsRejectedEvenWhenJsonAndHashLookValid()
    {
        var signature = _publisher.SignData(Payload(), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var changed = Envelope(Payload(size: 43), signature: signature);
        Should.Throw<InvalidDataException>(() => Verify(changed));
    }

    [Fact]
    public void UnknownKey_IsNotTrustedFromTheEnvelope()
    {
        Should.Throw<InvalidDataException>(() => Verify(Envelope(Payload(), keyId: "attacker")));
    }

    [Fact]
    public void SignatureFromAnotherKey_IsRejected()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Payload();
        var signature = attacker.SignData(payload, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Should.Throw<InvalidDataException>(() => Verify(Envelope(payload, signature: signature)));
    }

    [Fact]
    public void OversizedEnvelope_IsRejectedBeforeParsing()
    {
        Should.Throw<InvalidDataException>(() => Verify(new byte[1024 * 1024 + 1]));
    }

    [Fact]
    public void ReleaseTagPrefix_DoesNotChangeTheSignedVersionIdentity()
    {
        Verify(Envelope(Payload()), "v2.0.0").Version.ShouldBe("2.0.0");
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public void EqualOrOlderRelease_IsRejected(string version)
    {
        Should.Throw<InvalidDataException>(() => Verify(Envelope(Payload(version: version)), version));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("platform")]
    [InlineData("name")]
    [InlineData("commit")]
    [InlineData("digest")]
    [InlineData("size")]
    [InlineData("schema")]
    [InlineData("duplicate")]
    public void AuthenticatedButInapplicableMetadata_IsRejected(string mismatch)
    {
        var payload = Payload(
            version: mismatch == "version" ? "3.0.0" : "2.0.0",
            rid: mismatch == "platform" ? "osx-arm64" : "win-x64",
            name: mismatch == "name" ? "../installer.msi" : AssetName,
            commit: mismatch == "commit" ? "main" : Commit,
            digest: mismatch == "digest" ? "not-a-digest" : Digest,
            size: mismatch == "size" ? -1 : 42,
            schema: mismatch == "schema" ? 99 : 1,
            duplicate: mismatch == "duplicate");
        Should.Throw<InvalidDataException>(() => Verify(Envelope(payload)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not JSON")]
    [InlineData("{\"schemaVersion\":1,\"keyId\":\"publisher\",\"payload\":\"!\",\"signature\":\"!\"}")]
    public void MissingOrMalformedMetadata_IsRejected(string json)
    {
        Should.Throw<InvalidDataException>(() => Verify(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    private VerifiedReleaseArtifact Verify(byte[] envelope, string version = "2.0.0") =>
        new ReleaseManifestVerifier(new Dictionary<string, string>
        {
            ["publisher"] = _publisher.ExportSubjectPublicKeyInfoPem()
        }).Verify(envelope, version, new SemanticVersion(1, 0, 0), "win-x64", AssetName);

    private byte[] Envelope(byte[] payload, string keyId = "publisher", byte[]? signature = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            keyId,
            payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(signature ?? _publisher.SignData(payload,
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        });

    private static byte[] Payload(string version = "2.0.0", string rid = "win-x64",
        string name = AssetName, string commit = Commit, string digest = Digest,
        long size = 42, int schema = 1, bool duplicate = false)
    {
        var asset = new { runtimeIdentifier = rid, name, size, sha256 = digest };
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = schema, version, sourceCommit = commit,
            assets = duplicate ? new[] { asset, asset } : new[] { asset }
        });
    }

    public void Dispose() => _publisher.Dispose();
}
