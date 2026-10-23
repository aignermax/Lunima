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

    def test_build_error_remains_visible_before_many_warnings(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(runner, "ROOT", pathlib.Path(directory)):
            captured = io.StringIO()
            child = "print('source.cs(1): error CS0246: missing type'); print('source.cs(2): warning CA1822: advice\\n' * 200); raise SystemExit(1)"
            with contextlib.redirect_stdout(captured):
                result = runner.run([sys.executable, "-c", child], os.environ, 10)
            self.assertEqual(1, result)
            displayed_output = captured.getvalue().split("Full log:", 1)[1].split("\n", 1)[1]
            self.assertIn("error CS0246", displayed_output)
            self.assertNotIn("warning CA1822", displayed_output)
            log = next((pathlib.Path(directory) / "TestResults/local").glob("*.log"))
            self.assertIn("warning CA1822", log.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
