"""Exercise the exact APT fallback embedded in the composite action, without sudo."""
from pathlib import Path
import tempfile
import textwrap
import unittest
from unittest.mock import patch


ACTION = Path(__file__).resolve().parents[2] / ".github/actions/setup-test-tools/action.yml"


class AptMirrorFallbackTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        (self.root / "sources.list.d").mkdir()

    def write(self, relative, content):
        path = self.root / relative
        path.write_text(content, encoding="utf-8")
        return path

    def run_fallback(self):
        action = ACTION.read_text(encoding="utf-8")
        code = textwrap.dedent(action.split("sudo python3 - <<'PY'\n", 1)[1].split("\n        PY", 1)[0])
        real_path = Path

        def apt_path(value):
            relative = real_path(value).relative_to("/etc/apt")
            return self.root / relative

        with patch("pathlib.Path", side_effect=apt_path):
            exec(compile(code, str(ACTION), "exec"), {})

    def test_runner_mirror_list_is_rewritten_and_other_mirrors_are_preserved(self):
        original = (
            "http://azure.archive.ubuntu.com/ubuntu\tpriority:1\n"
            "https://archive.ubuntu.com/ubuntu/\tpriority:2\n"
            "https://security.ubuntu.com/ubuntu\n"
        )
        path = self.write("apt-mirrors.txt", original)
        self.run_fallback()
        self.assertEqual(path.read_text(), original.replace(
            "http://azure.archive.ubuntu.com/ubuntu", "https://archive.ubuntu.com/ubuntu"))

    def test_legacy_list_and_deb822_sources_still_work(self):
        paths = [self.write("sources.list", "deb http://azure.archive.ubuntu.com/ubuntu noble main\n"),
                 self.write("sources.list.d/ubuntu.sources", "URIs: https://azure.archive.ubuntu.com/ubuntu/\n")]
        self.run_fallback()
        self.assertEqual(paths[0].read_text(), "deb https://archive.ubuntu.com/ubuntu noble main\n")
        self.assertEqual(paths[1].read_text(), "URIs: https://archive.ubuntu.com/ubuntu/\n")

    def test_other_repository_hosts_and_paths_are_unchanged(self):
        original = (
            "https://azure.archive.ubuntu.com/ubuntu-other\n"
            "https://azure.archive.ubuntu.com.example/ubuntu\n"
            "https://packages.microsoft.com/ubuntu/26.04/prod\n"
        )
        path = self.write("apt-mirrors.txt", original)
        self.run_fallback()
        self.assertEqual(path.read_text(), original)

    def test_absent_optional_source_files_are_allowed(self):
        self.run_fallback()
        self.assertFalse((self.root / "apt-mirrors.txt").exists())


if __name__ == "__main__":
    unittest.main()
