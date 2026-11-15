"""The packaged-app smoke must pass a complete bundle and name what a broken one lacks."""
import contextlib
import importlib.util
import io
import json
import math
import os
import pathlib
import stat
import sys
import tempfile
import unittest


SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "smoke_published_app.py"
spec = importlib.util.spec_from_file_location("smoke_published_app", SCRIPT)
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)

DESIGNS = (smoke.SUMMARY_DESIGN, smoke.SIMULATION_DESIGN)
POSIX_ONLY = unittest.skipIf(os.name == "nt", "the fake app is a shebang script")

# Answers like `lunima agent …`; exits 97 unless it runs from a sandboxed copy with HOME beside it.
FAKE_APP = """#!{python}
import json, os, sys
from pathlib import Path
here = Path(__file__).resolve().parent
sandbox = here.parent.parent
if Path(os.environ["HOME"]).resolve() != sandbox / "home":
    sys.exit(97)
(here / "written-by-app").write_text("x")
if {leak!r}:
    print("info: Lunima starting")
args = sys.argv[1:]
if args == ["agent", "tools"]:
    print(json.dumps([{{"name": "get_design_summary"}}, {{"name": "simulate"}}], indent=2))
    sys.exit(0)
tool, design = args[2], Path(args[args.index("--design") + 1])
if not design.is_file():
    sys.exit(1)
if tool == "simulate":
    result = {{"success": True, "outputs": [{{"power": {power!r}}}, {{"power": 0.1}}]}}
else:
    result = {{"component_count": 35}}
print(json.dumps({{"tool": tool, "is_error": False, "result": result}}, indent=2))
sys.stderr.write("log: done\\n")
sys.exit({exit_code})
"""


class SmokeTestCase(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="lunima-smoke-test-")
        self.addCleanup(temp.cleanup)
        self.root = pathlib.Path(temp.name) / "publish"
        examples = self.root / "examples"
        (examples / "isa").mkdir(parents=True)
        manifest = {"examples": [{"file": name, "rank": rank} for rank, name in enumerate(DESIGNS)]}
        (examples / "examples.json").write_text(json.dumps(manifest), encoding="utf-8")
        for name in DESIGNS:
            (examples / name).write_text("{}", encoding="utf-8")
        for name in smoke.ISA_SAMPLES:
            (examples / "isa" / name).write_text("HALT\n", encoding="utf-8")
        (self.root / "scripts" / "openebl").mkdir(parents=True)
        for name in smoke.OPENEBL_SCRIPTS:
            (self.root / "scripts" / "openebl" / name).write_text("", encoding="utf-8")
        self.binary = self.write_app()

    def write_app(self, leak=False, power=0.25, exit_code=0):
        binary = self.root / "Lunima"
        binary.write_text(FAKE_APP.format(python=sys.executable, leak=leak, power=power,
                                          exit_code=exit_code), encoding="utf-8")
        binary.chmod(binary.stat().st_mode | stat.S_IXUSR)
        return binary

    def in_place(self):
        """The fake bundle as a PackagedApp, for checks that start nothing."""
        sandbox = self.root.parent / "sandbox"
        return smoke.PackagedApp(self.binary, self.root, sandbox)

    def run_main(self, *args):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            code = smoke.main([str(self.binary), *args])
        return code, output.getvalue()


class BundleContentTests(SmokeTestCase):
    def test_complete_bundle_passes_every_file_check(self):
        app = self.in_place()
        self.assertIn("all 2 designs", smoke.check_example_designs(app))
        smoke.bundled_files(smoke.ISA_SAMPLES_DIR, smoke.ISA_SAMPLES)(app)
        smoke.bundled_files(smoke.OPENEBL_DIR, smoke.OPENEBL_SCRIPTS)(app)

    def test_a_design_missing_from_the_manifest_is_named(self):
        (self.root / "examples" / smoke.SIMULATION_DESIGN).unlink()
        with self.assertRaises(smoke.CheckFailed) as failure:
            smoke.check_example_designs(self.in_place())
        self.assertIn(smoke.SIMULATION_DESIGN, str(failure.exception))
        self.assertNotIn(smoke.SUMMARY_DESIGN, str(failure.exception))

    def test_unreadable_or_absent_manifest_fails(self):
        manifest = self.root / "examples" / "examples.json"
        manifest.write_text("{not json", encoding="utf-8")
        self.assertRaises(smoke.CheckFailed, smoke.check_example_designs, self.in_place())
        manifest.unlink()
        self.assertRaises(smoke.CheckFailed, smoke.check_example_designs, self.in_place())

    def test_missing_openebl_script_and_isa_sample_are_named(self):
        (self.root / "scripts" / "openebl" / "openebl_verification.py").unlink()
        (self.root / "examples" / "isa" / "multiply-3x4.asm").unlink()
        for folder, names, missing in ((smoke.OPENEBL_DIR, smoke.OPENEBL_SCRIPTS, "openebl_verification.py"),
                                       (smoke.ISA_SAMPLES_DIR, smoke.ISA_SAMPLES, "multiply-3x4.asm")):
            with self.assertRaises(smoke.CheckFailed) as failure:
                smoke.bundled_files(folder, names)(self.in_place())
            self.assertIn(missing, str(failure.exception))

    def test_lookup_never_leaves_the_bundle(self):
        # A repository checkout around the publish folder must not stand in for the bundle.
        outside = self.root.parent / "examples"
        (self.root / "examples").rename(outside)
        self.assertIsNone(self.in_place().find_upwards(smoke.EXAMPLES))

    def test_bundle_root_is_the_app_for_a_macos_bundle(self):
        app = pathlib.Path("dist", "Lunima.app")
        self.assertEqual(app, smoke.bundle_root(app / "Contents" / "MacOS" / "CAP.Desktop"))
        self.assertEqual(self.root, smoke.bundle_root(self.binary))


class OutputValidationTests(unittest.TestCase):
    def test_sandbox_environment_moves_every_user_folder(self):
        with tempfile.TemporaryDirectory() as temp:
            sandbox = pathlib.Path(temp)
            base = {"PATH": "/bin", "HOME": "/home/real", "DOTNET_BUNDLE_EXTRACT_BASE_DIR": "/x"}
            environment = smoke.sandbox_environment(sandbox, base)
            self.assertEqual("/bin", environment["PATH"])
            self.assertNotIn("DOTNET_BUNDLE_EXTRACT_BASE_DIR", environment)
            for name in ("HOME", "USERPROFILE", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME",
                         "XDG_STATE_HOME", "APPDATA", "LOCALAPPDATA"):
                folder = pathlib.Path(environment[name])
                self.assertTrue(folder.is_dir(), name)
                self.assertEqual(sandbox, pathlib.Path(os.path.commonpath([sandbox, folder])), name)

    def test_log_line_before_the_json_is_reported(self):
        with self.assertRaises(smoke.CheckFailed) as failure:
            smoke.parse_json_stdout('info: starting\n{"tool": "x"}\n')
        self.assertIn("info: starting", str(failure.exception))
        self.assertEqual({"tool": "x"}, smoke.parse_json_stdout('\n{"tool": "x"}\n'))

    def test_plausible_output_powers_pass(self):
        self.assertIn("0.1389", smoke.validate_output_powers([0.13885926, 0.13885926]))
        smoke.validate_output_powers([0, 1])

    def test_implausible_output_powers_fail(self):
        for powers in ([], [0.0, 0.0], [math.nan], [math.inf], [1.5], [-0.1], [0.7, 0.7], [True], ["0.5"]):
            with self.subTest(powers=powers):
                self.assertRaises(smoke.CheckFailed, smoke.validate_output_powers, powers)


class ProcessTests(SmokeTestCase):
    @POSIX_ONLY
    def test_complete_app_passes_from_a_sandboxed_copy(self):
        code, output = self.run_main()
        self.assertEqual(0, code, output)
        self.assertIn(f"{len(smoke.CHECKS)}/{len(smoke.CHECKS)} checks passed", output)
        self.assertFalse((self.root / "written-by-app").exists(), "the published files were modified")

    @POSIX_ONLY
    def test_log_line_on_stdout_fails_every_launch(self):
        self.write_app(leak=True)
        code, output = self.run_main()
        self.assertEqual(1, code, output)
        self.assertEqual(3, output.count("stdout is not a single JSON document"), output)

    @POSIX_ONLY
    def test_failed_exit_code_shows_stderr(self):
        self.write_app(exit_code=1)
        code, output = self.run_main()
        self.assertEqual(1, code, output)
        self.assertIn("exited with 1", output)
        self.assertIn("stderr| log: done", output)

    @POSIX_ONLY
    def test_implausible_simulation_fails(self):
        self.write_app(power=1.5)
        code, output = self.run_main()
        self.assertEqual(1, code, output)
        self.assertIn("FAIL  simulate", output)

    def test_missing_binary_is_a_usage_error(self):
        self.binary = self.root / "absent"
        self.assertEqual(2, self.run_main()[0])

    @unittest.skipIf(sys.platform == "darwin", "--rosetta is valid on macOS")
    def test_rosetta_outside_macos_is_a_usage_error(self):
        self.assertEqual(2, self.run_main("--rosetta")[0])


if __name__ == "__main__":
    unittest.main()
