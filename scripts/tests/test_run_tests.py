"""Check that the supported test entry point keeps local expensive work opt-in."""
import json
import pathlib
import subprocess
import sys
import unittest


RUNNER = pathlib.Path(__file__).resolve().parents[1] / "run_tests.py"


class TestRunnerTests(unittest.TestCase):
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
