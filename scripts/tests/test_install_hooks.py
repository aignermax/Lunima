"""Hook setup must enforce the gate without replacing an existing user's hook."""
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest


SCRIPTS = pathlib.Path(__file__).resolve().parents[1]
INSTALLER = SCRIPTS / "install_hooks.py"


class HookInstallationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lunima-hook-")
        self.addCleanup(self.temp.cleanup)
        self.repo = pathlib.Path(self.temp.name)
        self.git("init", "--quiet")
        self.git("config", "user.email", "test@example.invalid")
        self.git("config", "user.name", "Hook Test")
        (self.repo / ".githooks").mkdir()
        (self.repo / "scripts").mkdir()
        shutil.copyfile(SCRIPTS.parent / ".githooks/pre-commit", self.repo / ".githooks/pre-commit")
        shutil.copyfile(SCRIPTS / "check_source_size.py", self.repo / "scripts/check_source_size.py")

    def git(self, *args, check=True):
        return subprocess.run(["git", *args], cwd=self.repo, check=check,
                              capture_output=True, text=True, encoding="utf-8")

    def install(self):
        return subprocess.run([sys.executable, str(INSTALLER), "--root", str(self.repo)],
                              capture_output=True, text=True, encoding="utf-8")

    def test_installs_and_is_idempotent(self):
        self.assertEqual(0, self.install().returncode)
        self.assertEqual(".githooks", self.git("config", "--get", "core.hooksPath").stdout.strip())
        self.assertEqual(0, self.install().returncode)

    def test_refuses_configured_custom_hooks_without_overwriting(self):
        self.git("config", "core.hooksPath", "custom-hooks")
        result = self.install()
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertEqual("custom-hooks", self.git("config", "--get", "core.hooksPath").stdout.strip())

    def test_refuses_existing_default_precommit(self):
        hook = self.repo / ".git/hooks/pre-commit"
        hook.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        result = self.install()
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertEqual("#!/bin/sh\nexit 0\n", hook.read_text(encoding="utf-8"))

    def test_actual_commit_rejects_1000_lines(self):
        result = self.install()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        (self.repo / "Oversized.cs").write_text("// line\n" * 1000, encoding="utf-8")
        self.git("add", ".")
        commit = self.git("commit", "-m", "must not commit", check=False)
        self.assertNotEqual(0, commit.returncode)
        self.assertIn("Oversized.cs: 1000", commit.stdout + commit.stderr)


if __name__ == "__main__":
    unittest.main()
