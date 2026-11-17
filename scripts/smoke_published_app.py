#!/usr/bin/env python3
"""Start the packaged Lunima app the way a downloader would and check it works headless.

Usage::

    python3 -I scripts/smoke_published_app.py <app-binary> [--rosetta] [--timeout SECONDS]

``<app-binary>`` is the executable a user downloads: ``Lunima`` / ``Lunima.exe`` of the
portable build, ``CAP.Desktop.exe`` in the MSI publish folder, or
``Lunima.app/Contents/MacOS/CAP.Desktop``. Its bundle (the executable's folder, or the whole
``.app``) is copied into a throw-away sandbox first, so neither the app's walk-up resource
lookup nor this script can fall back on a repository checkout around the publish folder, and
the published files stay untouched. Every launch gets a sandboxed HOME / XDG_* / APPDATA /
LOCALAPPDATA, a closed stdin and a timeout.

Checks: ``agent tools`` lists the tools; every design of the bundled examples/examples.json,
the ISA sample programs, the openEBL scripts and the Python helper scripts sit where the app
looks for them;
``get_design_summary`` opens a shipped design; ``simulate`` returns plausible output powers.
Every stdout must be a single JSON document, so a log line leaking onto it fails the run.

Exit code: 0 every check passed (or ``--rosetta`` without Rosetta 2: skipped, logged),
1 a check failed, 2 bad command line. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Callable, Dict, List, Mapping, Optional, Sequence, Tuple

DEFAULT_TIMEOUT_SECONDS = 120.0
EXCERPT_LINES = 15
POWER_TOLERANCE = 1e-6
SUMMARY_TOOL = "get_design_summary"
SIMULATE_TOOL = "simulate"
SUMMARY_DESIGN = "Logic Gate Full Adder.lun"
SIMULATION_DESIGN = "Mach-Zehnder Interferometer.lun"
EXAMPLES = Path("examples")
EXAMPLES_MANIFEST = "examples.json"
# IsaSampleProgramCatalog offers these from examples/isa/; the Run-program tour opens multiply-3x4.asm.
ISA_SAMPLES_DIR = Path("examples", "isa")
ISA_SAMPLES = ("count-to-5.asm", "add-two-numbers.asm", "multiply-3x4.asm", "mask-and-invert.asm")
# OpenEblSubmissionChecker runs both from the nearest scripts/openebl/ at or above the binary.
OPENEBL_DIR = Path("scripts", "openebl")
OPENEBL_SCRIPTS = ("openebl_submission_check.py", "openebl_verification.py")
# The Mode Solver and the PDK Import Wizard resolve these from the nearest scripts/ at or above the binary.
HELPER_SCRIPTS_DIR = Path("scripts")
HELPER_SCRIPTS = ("mode_solve.py", "parse_pdk.py")
ROSETTA_PREFIX = ("arch", "-x86_64")
ROSETTA_PROBE = ("arch", "-x86_64", "/usr/bin/true")
ROSETTA_PROBE_TIMEOUT_SECONDS = 30


class CheckFailed(Exception):
    """A smoke check found the packaged app broken; the message says how."""


def bundle_root(binary: Path) -> Path:
    """The folder a user downloads: the ``.app`` for a macOS bundle, else the binary's folder."""
    macos, contents = binary.parent, binary.parent.parent
    if macos.name == "MacOS" and contents.name == "Contents" and contents.parent.suffix == ".app":
        return contents.parent
    return binary.parent


def sandbox_environment(sandbox: Path, base: Mapping[str, str]) -> Dict[str, str]:
    """``base`` with every per-user folder moved into ``sandbox`` (created here)."""
    home = sandbox / "home"
    folders = {
        "HOME": home, "USERPROFILE": home,
        "XDG_CONFIG_HOME": home / ".config", "XDG_DATA_HOME": home / ".local" / "share",
        "XDG_STATE_HOME": home / ".local" / "state", "XDG_CACHE_HOME": home / ".cache",
        "APPDATA": home / "AppData" / "Roaming", "LOCALAPPDATA": home / "AppData" / "Local",
    }
    for folder in folders.values():
        folder.mkdir(parents=True, exist_ok=True)
    environment = dict(base)
    # A user's single-file app extracts native libraries to its default location, not an override.
    environment.pop("DOTNET_BUNDLE_EXTRACT_BASE_DIR", None)
    environment.update({name: str(folder) for name, folder in folders.items()})
    return environment


def excerpt(label: str, text: str, head: bool = False) -> str:
    """The last (or first) lines of ``text`` indented under ``label``; empty without text."""
    lines = text.strip().splitlines()
    lines = lines[:EXCERPT_LINES] if head else lines[-EXCERPT_LINES:]
    return "".join(f"\n        {label}| {line}" for line in lines)


def parse_json_stdout(stdout: str) -> object:
    """The one JSON document on stdout; anything else (a leaked log line) is a failure."""
    try:
        return json.loads(stdout)
    except ValueError as error:
        raise CheckFailed(f"stdout is not a single JSON document ({error})"
                          f"{excerpt('stdout', stdout, head=True)}")


class PackagedApp:
    """A bundle inside a sandbox, and how to start its executable there."""

    def __init__(self, binary: Path, root: Path, sandbox: Path,
                 prefix: Sequence[str] = (), timeout: float = DEFAULT_TIMEOUT_SECONDS):
        self.binary, self.root, self.sandbox = binary, root, sandbox
        self.prefix, self.timeout = tuple(prefix), timeout
        self.environment = sandbox_environment(sandbox, os.environ)
        self.launch_seconds: List[float] = []

    @classmethod
    def copy_into(cls, binary: Path, sandbox: Path, prefix: Sequence[str], timeout: float) -> "PackagedApp":
        """Copies the bundle around ``binary`` into ``sandbox`` and targets the copy."""
        root = bundle_root(binary)
        copy = sandbox / "bundle" / root.name
        shutil.copytree(root, copy, symlinks=True)
        return cls(copy / binary.relative_to(root), copy, sandbox, prefix, timeout)

    def shown(self, path: Path) -> str:
        """``path`` as the downloader sees it, relative to the bundle's parent."""
        return path.relative_to(self.root.parent).as_posix()

    def find_upwards(self, relative: Path) -> Optional[Path]:
        """Like the app: the nearest ``relative`` folder walking up from the binary, within the bundle."""
        folder = self.binary.parent
        while True:
            if (folder / relative).is_dir():
                return folder / relative
            if folder == self.root:
                return None
            folder = folder.parent

    def example(self, file_name: str) -> Path:
        """A shipped example design; fails when the bundle has none of that name."""
        examples = self.find_upwards(EXAMPLES)
        if examples is None or not (examples / file_name).is_file():
            raise CheckFailed(f"the bundle ships no {EXAMPLES.as_posix()}/{file_name}")
        return examples / file_name

    def run_json(self, *args: str) -> object:
        """Starts the app with ``args``; requires exit code 0 and a pure-JSON stdout."""
        command = [*self.prefix, str(self.binary), *args]
        shown = " ".join([self.binary.name, *args])
        started = time.monotonic()
        try:
            done = subprocess.run(command, cwd=self.sandbox, env=self.environment,
                                  stdin=subprocess.DEVNULL, capture_output=True, timeout=self.timeout)
        except subprocess.TimeoutExpired as expired:
            stderr = (expired.stderr or b"").decode("utf-8", "replace")
            raise CheckFailed(f"`{shown}` did not finish within {self.timeout:g} s{excerpt('stderr', stderr)}")
        except OSError as error:
            raise CheckFailed(f"`{shown}` could not start: {error}")
        finally:
            self.launch_seconds.append(time.monotonic() - started)
        stdout, stderr = (stream.decode("utf-8", "replace") for stream in (done.stdout, done.stderr))
        if done.returncode != 0:
            raise CheckFailed(f"`{shown}` exited with {done.returncode}"
                              f"{excerpt('stdout', stdout)}{excerpt('stderr', stderr)}")
        return parse_json_stdout(stdout)

    def call_tool(self, tool: str, design: Path) -> object:
        """``agent call TOOL --design DESIGN``; the tool's ``result`` when it reported no error."""
        document = self.run_json("agent", "call", tool, "--design", str(design))
        if not isinstance(document, dict) or document.get("is_error") is not False:
            raise CheckFailed(f"{tool} reported an error: {json.dumps(document)[:500]}")
        return document.get("result")


def require_files(app: PackagedApp, relative: Path, names: Sequence[str]) -> Path:
    """The folder the app finds for ``relative``; fails naming every file it lacks."""
    folder = app.find_upwards(relative)
    if folder is None:
        raise CheckFailed(f"no {relative.as_posix()}/ at or above {app.shown(app.binary)}")
    missing = [name for name in names if not (folder / name).is_file()]
    if missing:
        raise CheckFailed(f"{app.shown(folder)} lacks {len(missing)} file(s): {', '.join(missing)}")
    return folder


def check_tool_list(app: PackagedApp) -> str:
    """``agent tools`` prints a non-empty JSON list that offers the tools the smoke calls."""
    tools = app.run_json("agent", "tools")
    names = [tool.get("name") for tool in tools if isinstance(tool, dict)] if isinstance(tools, list) else []
    if not names:
        raise CheckFailed(f"`agent tools` listed no tools: {json.dumps(tools)[:300]}")
    absent = [tool for tool in (SUMMARY_TOOL, SIMULATE_TOOL) if tool not in names]
    if absent:
        raise CheckFailed(f"`agent tools` lacks {', '.join(absent)}")
    return f"{len(names)} tools, incl. {SUMMARY_TOOL} and {SIMULATE_TOOL}"


def check_example_designs(app: PackagedApp) -> str:
    """Every design the bundled examples.json lists exists beside it."""
    examples = require_files(app, EXAMPLES, (EXAMPLES_MANIFEST,))
    manifest = examples / EXAMPLES_MANIFEST
    try:
        entries = json.loads(manifest.read_text(encoding="utf-8"))["examples"]
    except (OSError, ValueError, KeyError, TypeError) as error:
        raise CheckFailed(f"{app.shown(manifest)} is unreadable: {error!r}")
    files = [entry.get("file") for entry in entries if isinstance(entry, dict)] if isinstance(entries, list) else []
    if not files:
        raise CheckFailed(f"{app.shown(manifest)} lists no designs")
    missing = [str(name) for name in files if not isinstance(name, str) or not (examples / name).is_file()]
    if missing:
        raise CheckFailed(f"{app.shown(manifest)} lists {len(missing)} design(s) missing from "
                          f"{app.shown(examples)}: {', '.join(missing)}")
    return f"all {len(files)} designs of {app.shown(manifest)} present"


def bundled_files(relative: Path, names: Sequence[str]) -> Callable[[PackagedApp], str]:
    """A check that ``names`` all sit in the ``relative`` folder the app resolves."""
    def check(app: PackagedApp) -> str:
        return f"{', '.join(names)} in {app.shown(require_files(app, relative, names))}"
    return check


def check_design_summary(app: PackagedApp) -> str:
    """The shipped full adder opens and has components."""
    result = app.call_tool(SUMMARY_TOOL, app.example(SUMMARY_DESIGN))
    count = result.get("component_count") if isinstance(result, dict) else None
    if isinstance(count, bool) or not isinstance(count, int) or count <= 0:
        raise CheckFailed(f"component_count is {count!r}: {json.dumps(result)[:300]}")
    return f"{count} components"


def validate_output_powers(powers: Sequence[object]) -> str:
    """Passive outputs: each power finite in [0, 1], some light arrives, none created."""
    if not powers:
        raise CheckFailed("the simulation reported no outputs")
    implausible = [power for power in powers
                   if isinstance(power, bool) or not isinstance(power, (int, float))
                   or not math.isfinite(power) or not 0 <= power <= 1 + POWER_TOLERANCE]
    if implausible:
        raise CheckFailed(f"output power outside [0, 1]: {implausible}")
    if max(powers) <= 0:
        raise CheckFailed("no light reaches any output")
    if sum(powers) > 1 + POWER_TOLERANCE:
        raise CheckFailed(f"outputs receive {sum(powers):.6g}, more than the injected power 1")
    return "output power " + ", ".join(f"{power:.4g}" for power in powers)


def check_simulation(app: PackagedApp) -> str:
    """The shipped MZI simulates through the binary with plausible output powers."""
    result = app.call_tool(SIMULATE_TOOL, app.example(SIMULATION_DESIGN))
    if not isinstance(result, dict) or result.get("success") is not True:
        raise CheckFailed(f"the simulation failed: {json.dumps(result)[:500]}")
    outputs = result.get("outputs") if isinstance(result.get("outputs"), list) else []
    return validate_output_powers([output.get("power") for output in outputs if isinstance(output, dict)])


CHECKS: Tuple[Tuple[str, Callable[[PackagedApp], str]], ...] = (
    ("agent tools", check_tool_list),
    ("bundled example designs", check_example_designs),
    ("bundled ISA samples", bundled_files(ISA_SAMPLES_DIR, ISA_SAMPLES)),
    ("bundled openEBL scripts", bundled_files(OPENEBL_DIR, OPENEBL_SCRIPTS)),
    ("bundled helper scripts", bundled_files(HELPER_SCRIPTS_DIR, HELPER_SCRIPTS)),
    (f"{SUMMARY_TOOL} on {SUMMARY_DESIGN}", check_design_summary),
    (f"{SIMULATE_TOOL} on {SIMULATION_DESIGN}", check_simulation),
)


def run_checks(app: PackagedApp) -> int:
    """Runs every check, prints one line each and returns how many failed."""
    failures = 0
    for name, check in CHECKS:
        launches = len(app.launch_seconds)
        try:
            status, detail = "PASS", check(app)
        except CheckFailed as error:
            status, detail = "FAIL", str(error)
            failures += 1
        seconds = sum(app.launch_seconds[launches:])
        timing = f" [{seconds:.1f} s]" if len(app.launch_seconds) > launches else ""
        print(f"  {status}  {name}: {detail}{timing}", flush=True)
    print(f"{len(CHECKS) - failures}/{len(CHECKS)} checks passed")
    return failures


def rosetta_available() -> bool:
    """Whether this Mac can run x86_64 code (Rosetta 2, or an Intel CPU)."""
    try:
        probe = subprocess.run(ROSETTA_PROBE, capture_output=True, timeout=ROSETTA_PROBE_TIMEOUT_SECONDS)
    except (OSError, subprocess.TimeoutExpired):
        return False
    return probe.returncode == 0


def report_rosetta_skip(binary: Path) -> None:
    """Logs, as a run annotation on GitHub Actions too, that the x86_64 binary was not started."""
    message = f"Rosetta 2 is not available on this machine: {binary} (x86_64) was NOT started; smoke skipped."
    print(f"SKIP: {message}", flush=True)
    if os.environ.get("GITHUB_ACTIONS") == "true":
        print(f"::warning title=Packaged-app smoke skipped::{message}", flush=True)


def parse_arguments(argv: Optional[Sequence[str]]) -> argparse.Namespace:
    """The command line; argparse exits with code 2 on a malformed one."""
    parser = argparse.ArgumentParser(description=(__doc__ or "").split("\n\n")[0])
    parser.add_argument("binary", type=Path, help="the published executable")
    parser.add_argument("--rosetta", action="store_true",
                        help="macOS: start an x86_64 binary through `arch -x86_64` (skips without Rosetta 2)")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT_SECONDS,
                        help=f"seconds per launch (default {DEFAULT_TIMEOUT_SECONDS:g})")
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    """Smoke-tests the binary named on the command line; returns the process exit code."""
    arguments = parse_arguments(argv)
    binary = arguments.binary.resolve()
    if not binary.is_file():
        print(f"error: {arguments.binary} is not a file", file=sys.stderr)
        return 2
    if arguments.rosetta and sys.platform != "darwin":
        print("error: --rosetta only applies on macOS", file=sys.stderr)
        return 2
    if arguments.rosetta and not rosetta_available():
        report_rosetta_skip(binary)
        return 0

    prefix = ROSETTA_PREFIX if arguments.rosetta else ()
    print(f"Lunima packaged-app smoke: {binary}" + (" (via arch -x86_64)" if prefix else ""), flush=True)
    sandbox = Path(tempfile.mkdtemp(prefix="lunima-smoke-"))
    try:
        app = PackagedApp.copy_into(binary, sandbox, prefix, arguments.timeout)
        print(f"  bundle {bundle_root(binary).name} copied into sandbox {sandbox}", flush=True)
        return 1 if run_checks(app) else 0
    finally:
        shutil.rmtree(sandbox, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
