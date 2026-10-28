"""Sign exactly the seven stable-release artifacts. Never persist or print private keys."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import sys

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, utils


MAXIMUM_ARTIFACT_BYTES = 2 * 1024 * 1024 * 1024
REGISTRY = Path(__file__).resolve().parents[2] / "CAP-DataAccess/Updates/publisher-keys.json"


def validate_version(version):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        raise ValueError("A stable major.minor.patch version is required")
    if any(int(part) > 2147483647 for part in version.split(".")):
        raise ValueError("Version exceeds the application version range")


def validate_commit(commit):
    if not re.fullmatch(r"(?:[a-f0-9]{40}|[a-f0-9]{64})", commit):
        raise ValueError("A full lowercase source commit is required")


def expected_assets(version):
    validate_version(version)
    return {
        f"Lunima-Setup-{version}.msi": "win-x64",
        f"Lunima-{version}-win-x64.zip": "win-x64",
        f"Lunima-{version}-linux-x64.tar.gz": "linux-x64",
        **{f"Lunima-{version}-{rid}.{suffix}": rid
           for rid in ("osx-arm64", "osx-x64") for suffix in ("dmg", "zip")},
    }


def artifact_inventory(directory, version):
    directory = Path(directory)
    expected = expected_assets(version)
    if {entry.name for entry in directory.iterdir()} != set(expected):
        raise ValueError("Release staging must contain exactly the seven expected artifacts")
    assets = []
    for name, rid in sorted(expected.items()):
        path = directory / name
        if path.is_symlink() or not path.is_file():
            raise ValueError("Release artifacts must be regular files, not links or directories")
        digest, size = hashlib.sha256(), 0
        with path.open("rb") as stream:
            while chunk := stream.read(1024 * 1024):
                size += len(chunk)
                if size > MAXIMUM_ARTIFACT_BYTES:
                    raise ValueError("Release artifact exceeds the updater size limit")
                digest.update(chunk)
        if size == 0:
            raise ValueError("Release artifacts cannot be empty")
        assets.append(dict(name=name, runtimeIdentifier=rid, size=size, sha256=digest.hexdigest()))
    return assets


def compact_json(value):
    return json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def sign_release(directory, version, commit, key_id, registry, private_pem):
    validate_commit(commit)
    assets = artifact_inventory(directory, version)
    if key_id not in registry:
        raise ValueError("Signing key ID is not pinned in the application registry")
    try:
        private = serialization.load_pem_private_key(private_pem, password=None)
        public = serialization.load_pem_public_key(registry[key_id].encode("ascii"))
    except (ValueError, TypeError, UnicodeError):
        raise ValueError("Invalid publisher key material") from None
    if not isinstance(private, ec.EllipticCurvePrivateKey) or not isinstance(private.curve, ec.SECP256R1):
        raise ValueError("The signing key must be ECDSA P-256")
    if not isinstance(public, ec.EllipticCurvePublicKey) or not isinstance(public.curve, ec.SECP256R1):
        raise ValueError("The pinned public key must be ECDSA P-256")
    if private.public_key().public_numbers() != public.public_numbers():
        raise ValueError("The signing secret does not match the pinned public key")
    payload = compact_json(dict(schemaVersion=1, version=version, sourceCommit=commit, assets=assets))
    der = private.sign(payload, ec.ECDSA(hashes.SHA256()))
    public.verify(der, payload, ec.ECDSA(hashes.SHA256()))
    r, s = utils.decode_dss_signature(der)
    signature = r.to_bytes(32, "big") + s.to_bytes(32, "big")
    return compact_json(dict(schemaVersion=1, keyId=key_id,
        payload=base64.b64encode(payload).decode("ascii"),
        signature=base64.b64encode(signature).decode("ascii"))) + b"\n"


def validate_identity(version, tag, commit, expected_commit, tag_commit):
    validate_version(version)
    for value in (commit, expected_commit, tag_commit):
        validate_commit(value)
    if tag != "v" + version or commit != expected_commit or commit != tag_commit:
        raise ValueError("Stable tag, checked-out source, and triggering source identity must match exactly")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("directory", "version", "tag", "commit", "expected-commit", "tag-commit", "key-id", "output"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        validate_identity(args.version, args.tag, args.commit, args.expected_commit, args.tag_commit)
        # The secret is passed through the environment only, never command arguments or a temporary key file.
        secret = os.environ.pop("LUNIMA_RELEASE_SIGNING_KEY_PEM", "")
        if not secret:
            raise ValueError("Protected publisher signing secret is not configured")
        registry = json.loads(REGISTRY.read_text(encoding="utf-8"))
        envelope = sign_release(args.directory, args.version, args.commit, args.key_id, registry, secret.encode())
        # Exclusive creation prevents silently retaining/replacing a stale envelope from another build.
        with Path(args.output).open("xb") as stream:
            stream.write(envelope)
        print("Publisher manifest signed for seven release artifacts")
        return 0
    except Exception:
        # Crypto/parser exceptions can quote their input; never emit exception text from a secret-bearing process.
        print("Release signing failed: check source identity, artifact inventory, and protected key configuration", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
