"""The size guard must reject oversized committed code, not unrelated editor state."""
import pathlib
import subprocess
import sys
import tempfile
import unittest


CHECKER = pathlib.Path(__file__).resolve().parents[1] / "check_source_size.py"


class SourceSizeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lunima-size-")
        self.addCleanup(self.temp.cleanup)
        self.repo = pathlib.Path(self.temp.name)
        self.git("init", "--quiet")
        self.git("config", "user.email", "test@example.invalid")
        self.git("config", "user.name", "Source Size Test")

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.repo, check=True,
                              capture_output=True).stdout

    def source(self, name, lines, trailing=True):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        text = "\n".join("// source" for _ in range(lines))
        if trailing:
            text += "\n"
        path.write_text(text, encoding="utf-8")
        return path

    def check(self, *args):
        return subprocess.run([sys.executable, str(CHECKER), "--root", str(self.repo), *args],
                              capture_output=True, text=True, encoding="utf-8")

    def test_accepts_999_lines_including_no_final_newline(self):
        self.source("Feature.cs", 999, trailing=False)
        self.git("add", "Feature.cs")
        result = self.check()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_rejects_exactly_1000_lines_with_path_and_count(self):
        self.source("Feature.cs", 1000)
        self.git("add", "Feature.cs")
        result = self.check()
        self.assertEqual(1, result.returncode, result.stderr)
        self.assertIn("Feature.cs", result.stdout)
        self.assertIn("1000", result.stdout)

    def test_staged_guard_ignores_unstaged_oversized_edit(self):
        self.source("Feature.cs", 999)
        self.git("add", "Feature.cs")
        self.source("Feature.cs", 1001)
        result = self.check("--staged")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_staged_guard_rejects_oversized_index_even_when_worktree_fixed(self):
        self.source("Feature.cs", 1000)
        self.git("add", "Feature.cs")
        self.source("Feature.cs", 20)
        result = self.check("--staged")
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)

    def test_staged_deleted_file_is_not_a_violation(self):
        self.source("Legacy.cs", 1100)
        self.git("add", "Legacy.cs")
        self.git("commit", "--quiet", "-m", "fixture")
        self.git("rm", "Legacy.cs")
        result = self.check("--staged")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_renamed_unicode_file_with_spaces_is_checked(self):
        self.source("Old.cs", 1000)
        self.git("add", "Old.cs")
        self.git("commit", "--quiet", "-m", "fixture")
        self.git("mv", "Old.cs", "Änderung with spaces.cs")
        result = self.check("--staged")
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("Änderung with spaces.cs", result.stdout)

    def test_checks_tests_views_scripts_and_workflows(self):
        names = ["UnitTests/FeatureTests.cs", "Views/View.axaml", "scripts/task.py",
                 "scripts/task.ps1", "docs/index.html", ".github/workflows/test.yml"]
        for name in names:
            self.source(name, 1000)
        self.git("add", ".")
        result = self.check()
        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        for name in names:
            self.assertIn(name, result.stdout)

    def test_count_is_physical_lines_not_unicode_separators(self):
        path = self.repo / "Feature.cs"
        path.write_text('// "a\u2028b"\n' * 999, encoding="utf-8")
        self.git("add", "Feature.cs")
        result = self.check()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
