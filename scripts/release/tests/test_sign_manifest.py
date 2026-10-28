"""Real signature tests; fixture private keys are generated only in memory."""
import base64
import hashlib
import json
import io
import os
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, utils
from scripts.release.sign_manifest import sign_release, validate_identity
from scripts.release import sign_manifest


VERSION = "2.3.4"
COMMIT = "a" * 40
NAMES = {
    "Lunima-Setup-2.3.4.msi": "win-x64",
    "Lunima-2.3.4-win-x64.zip": "win-x64",
    "Lunima-2.3.4-linux-x64.tar.gz": "linux-x64",
    "Lunima-2.3.4-osx-arm64.dmg": "osx-arm64",
    "Lunima-2.3.4-osx-x64.dmg": "osx-x64",
    "Lunima-2.3.4-osx-arm64.zip": "osx-arm64",
    "Lunima-2.3.4-osx-x64.zip": "osx-x64",
}


class SigningTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        for name in NAMES:
            (self.directory / name).write_bytes(name.encode())
        self.key = ec.generate_private_key(ec.SECP256R1())
        self.pem = self.key.private_bytes(serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
        self.registry = {"test-only": self.key.public_key().public_bytes(
            serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo).decode()}

    def sign(self, **changes):
        args = dict(directory=self.directory, version=VERSION, commit=COMMIT,
                    key_id="test-only", registry=self.registry, private_pem=self.pem)
        return sign_release(**(args | changes))

    def test_exact_inventory_is_bound_to_real_p1363_signature(self):
        envelope = json.loads(self.sign())
        self.assertEqual(1, envelope["schemaVersion"])
        self.assertEqual("test-only", envelope["keyId"])
        payload = base64.b64decode(envelope["payload"], validate=True)
        signature = base64.b64decode(envelope["signature"], validate=True)
        self.assertEqual(64, len(signature))
        der = utils.encode_dss_signature(int.from_bytes(signature[:32]), int.from_bytes(signature[32:]))
        self.key.public_key().verify(der, payload, ec.ECDSA(hashes.SHA256()))
        with self.assertRaises(InvalidSignature):
            self.key.public_key().verify(der, payload + b" ", ec.ECDSA(hashes.SHA256()))
        signed = json.loads(payload)
        self.assertEqual(VERSION, signed["version"])
        self.assertEqual(COMMIT, signed["sourceCommit"])
        self.assertEqual(NAMES, {a["name"]: a["runtimeIdentifier"] for a in signed["assets"]})
        for asset in signed["assets"]:
            data = (self.directory / asset["name"]).read_bytes()
            self.assertEqual(len(data), asset["size"])
            self.assertEqual(hashlib.sha256(data).hexdigest(), asset["sha256"])

    def test_missing_extra_and_empty_artifacts_are_rejected(self):
        first = self.directory / next(iter(NAMES))
        for failure in ("missing", "extra", "empty"):
            with self.subTest(failure=failure):
                first.write_bytes(b"fixture")
                extra = self.directory / "unsigned-extra.exe"
                if failure == "missing":
                    first.unlink()
                elif failure == "extra":
                    extra.write_bytes(b"extra")
                else:
                    first.write_bytes(b"")
                with self.assertRaises(ValueError):
                    self.sign()
                extra.unlink(missing_ok=True)

    def test_directory_disguised_as_artifact_is_rejected(self):
        first = self.directory / next(iter(NAMES))
        first.unlink()
        first.mkdir()
        with self.assertRaises(ValueError):
            self.sign()

    def test_symlink_and_oversized_artifact_are_rejected(self):
        with patch.object(Path, "is_symlink", return_value=True), self.assertRaises(ValueError):
            self.sign()
        with patch.object(sign_manifest, "MAXIMUM_ARTIFACT_BYTES", 2), self.assertRaises(ValueError):
            self.sign()

    def test_unknown_key_and_secret_not_matching_pin_are_rejected(self):
        with self.assertRaises(ValueError):
            self.sign(key_id="untrusted")
        other = ec.generate_private_key(ec.SECP256R1())
        wrong = other.private_bytes(serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
        with self.assertRaises(ValueError):
            self.sign(private_pem=wrong)

    def test_wrong_curve_is_rejected_even_with_matching_pin(self):
        other = ec.generate_private_key(ec.SECP384R1())
        secret = other.private_bytes(serialization.Encoding.PEM,
            serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
        pin = other.public_key().public_bytes(serialization.Encoding.PEM,
            serialization.PublicFormat.SubjectPublicKeyInfo).decode()
        with self.assertRaises(ValueError):
            self.sign(private_pem=secret, registry={"test-only": pin})

    def test_invalid_versions_and_commits_are_rejected(self):
        for version in ("2.3.4-beta", "02.3.4", "2.3", "2.3.4/evil", "2147483648.0.0"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                self.sign(version=version)
        for commit in ("main", "a" * 39, "g" * 40):
            with self.subTest(commit=commit), self.assertRaises(ValueError):
                self.sign(commit=commit)

    def test_release_identity_requires_exact_tag_checkout_and_event_commit(self):
        validate_identity(VERSION, "v" + VERSION, COMMIT, COMMIT, COMMIT)
        for tag, expected, tag_commit in (("v2.3.4-build.7", COMMIT, COMMIT),
                ("v" + VERSION, "b" * 40, COMMIT), ("v" + VERSION, COMMIT, "b" * 40)):
            with self.subTest(tag=tag, expected=expected, tag_commit=tag_commit), self.assertRaises(ValueError):
                validate_identity(VERSION, tag, COMMIT, expected, tag_commit)

    def test_cli_uses_environment_secret_and_never_prints_it(self):
        with tempfile.TemporaryDirectory() as config_directory:
            registry = Path(config_directory) / "keys.json"
            registry.write_text(json.dumps(self.registry), encoding="utf-8")
            output = Path(config_directory) / "release-manifest.json"
            arguments = ["sign_manifest", "--directory", str(self.directory), "--version", VERSION,
                "--tag", "v" + VERSION, "--commit", COMMIT, "--expected-commit", COMMIT,
                "--tag-commit", COMMIT, "--key-id", "test-only", "--output", str(output)]
            for secret, expected_code in ((self.pem.decode(), 0), ("SECRET_SENTINEL_invalid_pem", 1), ("", 1)):
                output.unlink(missing_ok=True)
                stdout, stderr = io.StringIO(), io.StringIO()
                with patch.object(sign_manifest, "REGISTRY", registry), patch("sys.argv", arguments), \
                     patch.dict(os.environ, {"LUNIMA_RELEASE_SIGNING_KEY_PEM": secret}), \
                     redirect_stdout(stdout), redirect_stderr(stderr):
                    self.assertEqual(expected_code, sign_manifest.main())
                self.assertEqual(expected_code == 0, output.exists())
                if secret:
                    self.assertNotIn(secret, stdout.getvalue() + stderr.getvalue())
                self.assertNotIn("PRIVATE KEY", stdout.getvalue() + stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
