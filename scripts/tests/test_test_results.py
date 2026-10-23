"""Exercise result acceptance with real child output, without launching .NET."""
import contextlib
import importlib.util
import io
import os
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location(
    "run_tests", pathlib.Path(__file__).resolve().parents[1] / "run_tests.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class TestResultAcceptanceTests(unittest.TestCase):
    def run_summary(self, summary):
        with tempfile.TemporaryDirectory() as directory, patch.object(runner, "ROOT", pathlib.Path(directory)):
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                return runner.run([sys.executable, "-c", "print(" + repr(summary) + ")"], os.environ, 10)

    def test_all_skipped_is_not_a_verified_run(self):
        self.assertEqual(1, self.run_summary("Passed! - Failed: 0, Passed: 0, Skipped: 6, Total: 6"))

    def test_empty_selection_is_not_a_verified_run(self):
        self.assertEqual(1, self.run_summary("No test matches the given testcase filter"))

    def test_executed_passing_cases_are_accepted(self):
        self.assertEqual(0, self.run_summary("Passed! - Failed: 0, Passed: 12, Skipped: 1, Total: 13"))


if __name__ == "__main__":
    unittest.main()
