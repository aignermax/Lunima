"""Check constrained test selection and honest reporting of incomplete runs."""
import contextlib
import importlib.util
import io
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


RUNNER = pathlib.Path(__file__).resolve().parents[1] / "run_tests.py"
SPEC = importlib.util.spec_from_file_location("run_tests", RUNNER)
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class TestRunnerTests(unittest.TestCase):
    def test_qa_uses_the_constrained_shared_runner(self):
        config = (RUNNER.parents[1] / ".agent.toml").read_text(encoding="utf-8")
        self.assertIn("test_cmd = 'python scripts/run_tests.py'", config.splitlines())

    def test_concurrent_run_is_rejected_before_launch_and_lock_is_released(self):
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(runner.tempfile, "gettempdir", return_value=directory):
            child = (
                "import importlib.util, sys; from unittest import mock; "
                f"spec = importlib.util.spec_from_file_location('runner', {str(RUNNER)!r}); "
                "runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(runner); "
                f"runner.tempfile.gettempdir = lambda: {directory!r}; "
                "launch = mock.Mock(return_value=0); runner.run = launch; "
                "code = runner.main([]); "
                "assert launch.call_count == int(code == 0); sys.exit(code)"
            )
            with runner.test_lock():
                blocked = subprocess.run([sys.executable, "-c", child],
                                         capture_output=True, text=True, timeout=10)
                self.assertEqual(2, blocked.returncode, blocked.stderr)
                self.assertIn("shared lock", blocked.stderr)
            released = subprocess.run([sys.executable, "-c", child],
                                      capture_output=True, text=True, timeout=10)
            self.assertEqual(0, released.returncode, released.stderr)

    def test_aborted_run_cannot_be_masked_by_a_passing_summary(self):
        self.assertEqual(137, self.run_child(137, "Passed! Failed: 0, Passed: 1, Total: 1"))

    def test_zero_exit_without_executed_tests_is_not_success(self):
        self.assertEqual(1, self.run_child(0, "Test run aborted: insufficient memory"))

    def test_completed_passing_run_is_success(self):
        self.assertEqual(0, self.run_child(0, "Passed! Failed: 0, Passed: 1, Total: 1"))

    def run_child(self, exit_code, output):
        command = [sys.executable, "-c", f"print({output!r}); raise SystemExit({exit_code})"]
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(runner, "ROOT", pathlib.Path(directory)), \
                contextlib.redirect_stdout(io.StringIO()), \
                contextlib.redirect_stderr(io.StringIO()):
            return runner.run(command, os.environ.copy(), timeout=10)

    def dry_run(self, *args):
        result = subprocess.run([sys.executable, str(RUNNER), "--dry-run", *args],
                                capture_output=True, text=True, encoding="utf-8")
        self.assertEqual(0, result.returncode, result.stderr)
        return json.loads(result.stdout)

    def test_default_excludes_both_expensive_categories(self):
        data = self.dry_run()
        command = data["command"]
        self.assertEqual("Category!=Slow&Category!=Heavy", command[command.index("--filter") + 1])
        self.assertEqual("2", data["environment"]["DOTNET_PROCESSOR_COUNT"])

    def test_substring_filter_remains_constrained(self):
        data = self.dry_run("CrossingUndo")
        self.assertIn("(FullyQualifiedName~CrossingUndo)&Category!=Slow&Category!=Heavy", data["command"])

    def test_full_filter_is_grouped_before_exclusions(self):
        data = self.dry_run("--filter", "FullyQualifiedName~One|FullyQualifiedName~Two")
        self.assertIn("(FullyQualifiedName~One|FullyQualifiedName~Two)&Category!=Slow&Category!=Heavy",
                      data["command"])

    def test_expensive_selection_requires_specific_filter(self):
        result = subprocess.run([sys.executable, str(RUNNER), "--dry-run", "--include-expensive"],
                                capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("specific filter", result.stderr)

    def test_selected_expensive_tests_are_possible(self):
        data = self.dry_run("RepeatedExampleLoadTests", "--include-expensive")
        self.assertIn("FullyQualifiedName~RepeatedExampleLoadTests", data["command"])
        self.assertNotIn("Category!=Slow&Category!=Heavy", data["command"])


if __name__ == "__main__":
    unittest.main()
