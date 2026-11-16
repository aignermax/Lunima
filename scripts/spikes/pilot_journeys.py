#!/usr/bin/env python3
"""Spike #1647: run the three pilot journeys through an installed Lunima release's agent CLI.

Usage::

    python3 -I scripts/spikes/pilot_journeys.py <installed-binary> --gds FILE --phase NAME
        --out RESULT.json [--journeys 1,2,3] [--python EXE] [--klayout EXE] [--timeout S]

The binary is started where the installer put it (nothing is copied), from a sandbox working
folder with a fresh HOME / APPDATA / LOCALAPPDATA, so neither a repository checkout nor the
runner's user profile is reachable (``scripts/smoke_published_app.py`` provides the sandbox).

Journeys:
1. ``simulate`` the shipped Mach-Zehnder example and read the port powers.
2. ``analyze_gds`` + ``import_gds`` a GDS with port labels, then ``get_component`` the pins.
3. ``export_gds`` the shipped EBeam Add-Drop Ring, ``check_foundry_drc`` it with the shipped
   Cornerstone deck, and look for an openEBL pre-flight among the CLI tools. As a side
   measurement (not the CLI) the shipped openEBL script runs on the exported GDS with --python.

Each outcome keeps the app's own messages, so the report can say whether the app named a
missing dependency. Exit code 0 unless the command line is bad (2); verdicts live in --out.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Sequence

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import smoke_published_app as smoke  # noqa: E402

MZI_DESIGN = "Mach-Zehnder Interferometer.lun"
RING_DESIGN = "EBeam Add-Drop Ring.lun"
CORNERSTONE_DECK = "cornerstone_sin300_drc"
OPENEBL_DIR = Path("scripts", "openebl")
OPENEBL_SCRIPTS = ("openebl_submission_check.py", "openebl_verification.py")
EXPORT_SCRIPT_NAME = "pilot_chip.py"
KLAYOUT_MISSING = "KLayout was not found"
HEURISTIC_PIN_PREFIX = "heur_"
MESSAGE_LIMIT = 600
SIDE_TIMEOUT_SECONDS = 600
# PythonDiscoveryService.GetPackageVersion gives `import nazca` this long before it gives up.
APP_NAZCA_PROBE_SECONDS = 20
PROBE_ATTEMPTS = 2


@dataclass
class Launch:
    """One start of the binary: exit code, parsed stdout (or None) and both streams."""
    exit_code: Optional[int]
    document: object
    stdout: str
    stderr: str
    seconds: float

    def describe(self) -> str:
        """A short failure text: exit code plus the stream tails."""
        return (f"exit {self.exit_code}{smoke.excerpt('stdout', self.stdout)}"
                f"{smoke.excerpt('stderr', self.stderr)}")


@dataclass
class Outcome:
    """The verdict of one journey in one phase."""
    journey: str
    status: str
    detail: str
    messages: List[str] = field(default_factory=list)
    extra_environment: List[str] = field(default_factory=list)
    seconds: float = 0.0


class InstalledApp:
    """The installed binary, started from a sandbox with a fresh per-user profile."""

    def __init__(self, binary: Path, sandbox: Path, timeout: float, venv: Optional[Path] = None):
        self.binary, self.sandbox, self.timeout = binary, sandbox, timeout
        self.root = smoke.bundle_root(binary)
        self.environment = smoke.sandbox_environment(sandbox, os.environ)
        if venv is not None:
            self.environment["VIRTUAL_ENV"] = str(venv)
            self.environment["PATH"] = str(venv_bin(venv)) + os.pathsep + self.environment.get("PATH", "")

    def launch(self, *args: str, extra_env: Optional[Dict[str, str]] = None) -> Launch:
        """Starts the binary; never raises for a non-zero exit or a non-JSON stdout."""
        environment = {**self.environment, **(extra_env or {})}
        started = time.monotonic()
        try:
            done = subprocess.run([str(self.binary), *args], cwd=self.sandbox, env=environment,
                                  stdin=subprocess.DEVNULL, capture_output=True, timeout=self.timeout)
        except subprocess.TimeoutExpired as expired:
            stderr = (expired.stderr or b"").decode("utf-8", "replace")
            return Launch(None, None, "", f"timed out after {self.timeout:g} s\n{stderr}",
                          time.monotonic() - started)
        stdout, stderr = (stream.decode("utf-8", "replace") for stream in (done.stdout, done.stderr))
        try:
            document = json.loads(stdout)
        except ValueError:
            document = None
        return Launch(done.returncode, document, stdout, stderr, time.monotonic() - started)

    def shipped(self, relative: Path) -> Optional[Path]:
        """The nearest ``relative`` at or above the binary, within the installed bundle."""
        folder = self.binary.parent
        while True:
            if (folder / relative).exists():
                return folder / relative
            if folder == self.root or folder == folder.parent:
                return None
            folder = folder.parent

    def example(self, name: str) -> Path:
        """A shipped example design; raises CheckFailed when the install lacks it."""
        found = self.shipped(Path("examples", name))
        if found is None:
            raise smoke.CheckFailed(f"the installation ships no examples/{name}")
        return found


def venv_bin(venv: Path) -> Path:
    """The folder of a virtual environment's interpreter (``Scripts`` on Windows)."""
    return venv / ("Scripts" if os.name == "nt" else "bin")


def venv_python(venv: Path) -> Path:
    """A virtual environment's interpreter."""
    return venv_bin(venv) / ("python.exe" if os.name == "nt" else "python")


def clip(text: object) -> str:
    """``text`` as one string of at most MESSAGE_LIMIT characters."""
    value = text if isinstance(text, str) else json.dumps(text)
    return value if len(value) <= MESSAGE_LIMIT else value[:MESSAGE_LIMIT] + "…"


def steps_of(launch: Launch) -> List[dict]:
    """The step objects of an ``agent run`` (a list) or ``agent call`` (one object)."""
    document = launch.document
    if isinstance(document, dict):
        return [document]
    return [step for step in document if isinstance(step, dict)] if isinstance(document, list) else []


def run_script(app: InstalledApp, steps: Sequence[dict], name: str, *extra: str) -> Launch:
    """``agent run`` over ``steps`` written to a script file in the sandbox."""
    script = app.sandbox / f"{name}.json"
    script.write_text(json.dumps(list(steps)), encoding="utf-8")
    return app.launch("agent", "run", str(script), "--keep-going", *extra)


def journey_simulation(app: InstalledApp) -> Outcome:
    """Journey 1: the shipped MZI simulates and reports plausible port powers."""
    launch = app.launch("agent", "call", "simulate", "--design", str(app.example(MZI_DESIGN)))
    step = (steps_of(launch) or [{}])[0]
    result = step.get("result")
    if launch.exit_code != 0 or step.get("is_error") is not False or not isinstance(result, dict):
        return Outcome("1 simulate MZI", "FAIL", launch.describe(), [clip(result)], seconds=launch.seconds)
    outputs = [output for output in result.get("outputs") or [] if isinstance(output, dict)]
    powers = smoke.validate_output_powers([output.get("power") for output in outputs])
    ports = [output.get("name") or output.get("component") or output.get("pin") for output in outputs]
    return Outcome("1 simulate MZI", "PASS", f"{powers} at {ports}", seconds=launch.seconds)


def journey_gds_import(app: InstalledApp, gds: Path) -> Outcome:
    """Journey 2: a GDS with port labels imports into components that carry pins."""
    saved = app.sandbox / "imported.lun"
    steps = [{"tool": "analyze_gds", "arguments": {"path": str(gds)}},
             {"tool": "import_gds", "arguments": {"path": str(gds), "group": False}},
             {"tool": "list_components", "arguments": {}}]
    first = run_script(app, steps, "import", "--save", str(saved))
    results = steps_of(first)
    failed = [step for step in results if step.get("is_error") is not False]
    if first.exit_code != 0 or len(results) != len(steps) or failed:
        return Outcome("2 import GDS", "FAIL", first.describe(), [clip(step.get("result")) for step in failed],
                       seconds=first.seconds)
    imported, listing = results[1]["result"], results[2]["result"]
    ids = [component.get("id") for component in listing.get("components", []) if isinstance(component, dict)]
    second = run_script(app, [{"tool": "get_component", "arguments": {"id": i}} for i in ids],
                        "pins", "--design", str(saved))
    pins = {str(step["result"].get("id") or step["result"].get("name")):
            [pin.get("name") for pin in step["result"].get("pins") or []]
            for step in steps_of(second) if step.get("is_error") is False and isinstance(step.get("result"), dict)}
    names = [name for component in pins.values() for name in component]
    guessed = sum(1 for name in names if str(name).startswith(HEURISTIC_PIN_PREFIX))
    labelled = len(names) - guessed
    detail = (f"placed {imported.get('placed')}, connected {imported.get('connected')}, "
              f"{labelled} labelled + {guessed} heuristic pins: {json.dumps(pins)}")
    status = "PASS" if imported.get("placed") and labelled > 0 else "FAIL"
    return Outcome("2 import GDS", status, clip(detail), [clip(w) for w in imported.get("warnings") or []],
                   seconds=first.seconds + second.seconds)


def export_ring(app: InstalledApp) -> tuple:
    """``export_gds`` of the shipped ring: (gds path or None, messages, launch)."""
    script = app.sandbox / "export" / EXPORT_SCRIPT_NAME
    launch = app.launch("agent", "call", "export_gds", json.dumps({"path": str(script)}),
                        "--design", str(app.example(RING_DESIGN)))
    step = (steps_of(launch) or [{}])[0]
    result = step.get("result") if isinstance(step.get("result"), dict) else {}
    gds = result.get("gds_path")
    messages = [clip(result.get("gds_status") or step.get("result"))]
    messages += [clip(w) for w in result.get("warnings") or []]
    return (Path(gds) if gds and Path(gds).is_file() else None), messages, launch


def gds_by_hand(app: InstalledApp, python: Optional[str]) -> Optional[Path]:
    """Not the CLI: runs the Nazca script export_gds wrote with ``python``; the GDS or None."""
    script = app.sandbox / "export" / EXPORT_SCRIPT_NAME
    if not python or not script.is_file():
        return None
    subprocess.run([python, script.name], cwd=script.parent, capture_output=True,
                   timeout=SIDE_TIMEOUT_SECONDS, stdin=subprocess.DEVNULL)
    gds = script.with_suffix(".gds")
    return gds if gds.is_file() else None


def foundry_drc(app: InstalledApp, gds: Path, klayout: Optional[str]) -> Outcome:
    """The shipped Cornerstone deck on ``gds``; retries with KLAYOUT when the app asks for it."""
    arguments = json.dumps({"deck": CORNERSTONE_DECK, "gds_path": str(gds)})
    launch = app.launch("agent", "call", "check_foundry_drc", arguments)
    extra: List[str] = []
    step = (steps_of(launch) or [{}])[0]
    if KLAYOUT_MISSING in json.dumps(step.get("result")) and klayout:
        extra.append(f"KLAYOUT={klayout}")
        launch = app.launch("agent", "call", "check_foundry_drc", arguments, extra_env={"KLAYOUT": klayout})
        step = (steps_of(launch) or [{}])[0]
    result = step.get("result")
    if step.get("is_error") is not False or not isinstance(result, dict):
        return Outcome("3b Cornerstone DRC", "FAIL", launch.describe(), [clip(result)], extra, launch.seconds)
    rules = [f"{rule.get('rule')}={rule.get('violations')}" for rule in result.get("rules") or []]
    detail = f"deck ran: {result.get('total')} markers, clean={result.get('clean')}; {', '.join(rules)}"
    return Outcome("3b Cornerstone DRC", "PASS", clip(detail), [], extra, launch.seconds)


def openebl_via_cli(app: InstalledApp) -> Outcome:
    """Whether the agent CLI offers any openEBL pre-flight tool at all."""
    launch = app.launch("agent", "tools")
    names = [tool.get("name") for tool in launch.document or [] if isinstance(tool, dict)]
    matches = [name for name in names if name and "ebl" in name.lower()]
    if matches:
        return Outcome("3c openEBL pre-flight (CLI)", "PASS", f"tools {matches}")
    return Outcome("3c openEBL pre-flight (CLI)", "CANNOT_RUN",
                   f"none of the {len(names)} agent tools runs the openEBL check (GUI only)")


def openebl_side_measurement(app: InstalledApp, gds: Optional[Path], python: Optional[str]) -> Outcome:
    """Not the CLI: both shipped openEBL scripts, run by hand with ``python`` on the exported GDS."""
    scripts = [app.shipped(OPENEBL_DIR / name) for name in OPENEBL_SCRIPTS]
    name = "3d openEBL scripts (by hand)"
    if None in scripts or gds is None or not python:
        reason = "a shipped script is missing" if None in scripts else "no exported GDS" if gds is None else "no --python"
        return Outcome(name, "CANNOT_RUN", reason)
    started, verdicts, details = time.monotonic(), [], []
    for script in scripts:
        done = subprocess.run([python, str(script), str(gds)], cwd=app.sandbox, capture_output=True,
                              text=True, timeout=SIDE_TIMEOUT_SECONDS, stdin=subprocess.DEVNULL)
        lines = done.stdout.strip().splitlines()
        errors = lines[-1].strip() if lines else ""
        verdicts.append(done.returncode == 0 and errors.isdigit())
        categories = [line for line in lines if line.startswith("category ")]
        details.append(f"{script.name}: exit {done.returncode}, errors {errors or '?'} {categories}"
                       f"{smoke.excerpt('stderr', done.stderr)}")
    return Outcome(name, "PASS" if all(verdicts) else "FAIL", clip("; ".join(details)), [],
                   seconds=time.monotonic() - started)


def nazca_probe(app: InstalledApp, python: Optional[str]) -> Outcome:
    """The app's own Nazca check (``import nazca``, 20 s) repeated in the app's environment."""
    name = "3e diag: app's import-nazca probe"
    if not python:
        return Outcome(name, "CANNOT_RUN", "no venv / --python")
    attempts = []
    for _ in range(PROBE_ATTEMPTS):
        started = time.monotonic()
        try:
            done = subprocess.run([python, "-c", "import nazca; print(nazca.__version__)"], cwd=app.sandbox,
                                  env=app.environment, capture_output=True, text=True,
                                  timeout=SIDE_TIMEOUT_SECONDS, stdin=subprocess.DEVNULL)
            code, tail = done.returncode, (done.stdout + done.stderr).strip().splitlines()[-3:]
        except subprocess.TimeoutExpired:
            code, tail = None, ["no result"]
        attempts.append((code, time.monotonic() - started, tail))
    ok = all(code == 0 and seconds < APP_NAZCA_PROBE_SECONDS for code, seconds, _ in attempts)
    detail = "; ".join(f"exit {code} after {seconds:.1f} s: {' | '.join(tail)}" for code, seconds, tail in attempts)
    return Outcome(name, "PASS" if ok else "FAIL", clip(detail))


def journey_export(app: InstalledApp, python: Optional[str], klayout: Optional[str]) -> List[Outcome]:
    """Journey 3: export to GDS, then the Cornerstone and openEBL pre-flights."""
    gds, messages, launch = export_ring(app)
    export = Outcome("3a export GDS", "PASS" if gds else "FAIL",
                     f"gds {gds.name} ({gds.stat().st_size} B)" if gds
                     else f"no GDS produced (exit {launch.exit_code}){smoke.excerpt('stderr', launch.stderr)}",
                     messages, seconds=launch.seconds)
    outcomes = [export]
    if gds is None and (gds := gds_by_hand(app, python)) is not None:
        outcomes.append(Outcome("3a' GDS by hand from the exported script", "PASS",
                                f"python {EXPORT_SCRIPT_NAME} wrote {gds.name}; 3b/3d below check that GDS"))
    outcomes.append(foundry_drc(app, gds, klayout) if gds else Outcome(
        "3b Cornerstone DRC", "FAIL", "no GDS to check: the export produced none"))
    return outcomes + [openebl_via_cli(app), openebl_side_measurement(app, gds, python), nazca_probe(app, python)]


def run_journeys(app: InstalledApp, journeys: Sequence[str], gds: Path,
                 python: Optional[str], klayout: Optional[str]) -> List[Outcome]:
    """Runs the selected journeys; an unexpected CheckFailed turns into a FAIL outcome."""
    outcomes: List[Outcome] = []
    plan = {"1": lambda: [journey_simulation(app)], "2": lambda: [journey_gds_import(app, gds)],
            "3": lambda: journey_export(app, python, klayout)}
    for journey in journeys:
        try:
            outcomes += plan[journey]()
        except smoke.CheckFailed as error:
            outcomes.append(Outcome(f"{journey} (aborted)", "FAIL", str(error)))
    return outcomes


def parse_arguments(argv: Optional[Sequence[str]]) -> argparse.Namespace:
    """The command line; argparse exits with code 2 on a malformed one."""
    parser = argparse.ArgumentParser(description=(__doc__ or "").split("\n\n")[0])
    parser.add_argument("binary", type=Path, help="the installed executable")
    parser.add_argument("--gds", type=Path, required=True, help="a GDS with port labels (journey 2)")
    parser.add_argument("--phase", required=True, help="label of the provisioning phase")
    parser.add_argument("--out", type=Path, required=True, help="where the JSON result goes")
    parser.add_argument("--label", default=sys.platform, help="OS / package label for the report")
    parser.add_argument("--journeys", default="1,2,3", help="comma-separated subset of 1,2,3")
    parser.add_argument("--venv", type=Path, help="a virtual environment the app sees as activated")
    parser.add_argument("--python", help="interpreter for the by-hand openEBL side measurement "
                                         "(default: the --venv interpreter)")
    parser.add_argument("--klayout", help="KLayout executable to offer via KLAYOUT when the app asks")
    parser.add_argument("--timeout", type=float, default=smoke.DEFAULT_TIMEOUT_SECONDS * 3)
    return parser.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    """Runs the journeys and writes the outcomes to --out; returns the process exit code."""
    arguments = parse_arguments(argv)
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    binary = arguments.binary.resolve()
    if not binary.is_file() or not arguments.gds.is_file():
        print(f"error: {arguments.binary} or {arguments.gds} is not a file", file=sys.stderr)
        return 2
    journeys = [part.strip() for part in arguments.journeys.split(",") if part.strip()]
    if any(journey not in ("1", "2", "3") for journey in journeys):
        print("error: --journeys takes a subset of 1,2,3", file=sys.stderr)
        return 2
    sandbox = Path(tempfile.mkdtemp(prefix="lunima-pilot-"))
    venv = arguments.venv.resolve() if arguments.venv else None
    app = InstalledApp(binary, sandbox, arguments.timeout, venv)
    python = arguments.python or (str(venv_python(venv)) if venv else None)
    print(f"Pilot journeys [{arguments.label} / {arguments.phase}] on {binary}", flush=True)
    outcomes = run_journeys(app, journeys, arguments.gds.resolve(), python, arguments.klayout)
    for outcome in outcomes:
        print(f"  {outcome.status:<10} {outcome.journey}: {outcome.detail} [{outcome.seconds:.1f} s]", flush=True)
        for message in outcome.messages:
            print(f"             app said: {message}", flush=True)
    arguments.out.parent.mkdir(parents=True, exist_ok=True)
    arguments.out.write_text(json.dumps({"label": arguments.label, "phase": arguments.phase,
                                         "binary": str(binary), "outcomes": [asdict(o) for o in outcomes]},
                                        indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
