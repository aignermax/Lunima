#!/usr/bin/env python3
"""Run local xUnit/VSTest checks serially with bounded CPU and concise output."""
import argparse
import contextlib
import datetime
import json
import os
import pathlib
import re
import signal
import subprocess
import sys
import tempfile


ROOT = pathlib.Path(__file__).resolve().parents[1]
EXCLUSIONS = "Category!=Slow&Category!=Heavy"


@contextlib.contextmanager
def test_lock():
    """One advisory lock across all Lunima worktrees for this user's temp area."""
    path = pathlib.Path(tempfile.gettempdir()) / "lunima-dotnet-tests.lock"
    with path.open("a+b") as handle:
        handle.seek(0, os.SEEK_END)
        if handle.tell() == 0:
            handle.write(b"0")
            handle.flush()
        handle.seek(0)
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            if os.name == "nt":
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle, fcntl.LOCK_UN)


def stop_tree(process):
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait()


def run(command, environment, timeout):
    log_dir = ROOT / "TestResults" / "local"
    log_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    log = log_dir / f"tests-{stamp}-{os.getpid()}.log"
    launch = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {
        "start_new_session": True}
    print("Running:", subprocess.list2cmdline(command), flush=True)
    print("Full log:", log, flush=True)
    with log.open("w", encoding="utf-8") as output:
        process = subprocess.Popen(command, cwd=ROOT, env=environment,
                                   stdout=output, stderr=subprocess.STDOUT, **launch)
        try:
            code = process.wait(timeout=timeout)
        except (subprocess.TimeoutExpired, KeyboardInterrupt):
            stop_tree(process)
            print("Test run canceled or timed out; its process tree was stopped.", file=sys.stderr)
            code = 124
        finally:
            stop_tree(process)
    lines = log.read_text(encoding="utf-8", errors="replace").splitlines()
    visible = [line for line in lines if not re.search(r"\bwarning [A-Za-z]+\d+:", line)]
    tail = visible[-(18 if code == 0 else 100):]
    earlier_errors = [line for line in visible[:-len(tail)]
                      if re.search(r"\berror [A-Za-z]+\d+:", line)] if tail else []
    print("\n".join(earlier_errors + tail))
    summaries = [re.search(r"Passed!.*Failed:\s*0,\s*Passed:\s*([1-9]\d*),.*Total:", line)
                 for line in lines]
    if code == 0 and not any(summaries):
        print("No executed passing tests found; refusing to report success.", file=sys.stderr)
        return 1
    return code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pattern", nargs="?", help="fully qualified name substring")
    parser.add_argument("--filter", help="explicit VSTest filter expression")
    parser.add_argument("--include-expensive", action="store_true",
                        help="allow Slow/Heavy only for a specifically selected run")
    parser.add_argument("--timeout", type=int, default=900, help="overall timeout in seconds")
    parser.add_argument("--dry-run", action="store_true", help="print the exact command as JSON")
    args = parser.parse_args(argv)
    if args.pattern and args.filter:
        parser.error("use a pattern or --filter, not both")
    selected = args.filter or (f"FullyQualifiedName~{args.pattern}" if args.pattern else "")
    if args.include_expensive and not selected.strip():
        parser.error("--include-expensive requires a specific filter")
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    if args.pattern and any(char in args.pattern for char in "|&()=!"):
        parser.error("use --filter for a filter expression")
    expression = selected if args.include_expensive else (
        f"({selected})&{EXCLUSIONS}" if selected else EXCLUSIONS)
    command = ["dotnet", "test", str(ROOT / "UnitTests" / "UnitTests.csproj"),
               "--filter", expression, "--logger", "console;verbosity=minimal", "-m:1"]
    overrides = {"DOTNET_PROCESSOR_COUNT": "2", "DOTNET_CLI_UI_LANGUAGE": "en",
                 "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}
    if args.dry_run:
        print(json.dumps({"command": command, "environment": overrides}, indent=2))
        return 0
    try:
        with test_lock():
            return run(command, {**os.environ, **overrides}, args.timeout)
    except (OSError, subprocess.SubprocessError) as exc:
        print(f"Cannot run tests (another run may hold the shared lock): {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    sys.exit(main())
