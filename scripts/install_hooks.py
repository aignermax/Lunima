#!/usr/bin/env python3
"""Enable this repository's source guard, preserving existing user hook setups."""
import argparse
import pathlib
import subprocess
import sys


def git(root, *args, check=True):
    return subprocess.run(["git", "-C", str(root), *args], check=check,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          text=True, encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[1])
    args = parser.parse_args(argv)
    try:
        root = pathlib.Path(git(args.root, "rev-parse", "--show-toplevel").stdout.strip())
        configured = git(root, "config", "--get", "core.hooksPath", check=False).stdout.strip()
        if configured == ".githooks":
            print("Lunima hooks are already enabled.")
            return 0
        if configured:
            print(f"Existing core.hooksPath={configured!r} was preserved. "
                  "Add the staged source check to that hook setup explicitly.", file=sys.stderr)
            return 1
        default = pathlib.Path(git(root, "rev-parse", "--git-path", "hooks/pre-commit").stdout.strip())
        if not default.is_absolute():
            default = root / default
        if default.exists():
            print("Existing pre-commit hook was preserved. Add the staged source check to it explicitly.",
                  file=sys.stderr)
            return 1
        hook = root / ".githooks/pre-commit"
        if not hook.is_file() or not (root / "scripts/check_source_size.py").is_file():
            print("This checkout does not contain the Lunima source guard.", file=sys.stderr)
            return 1
        hook.chmod(hook.stat().st_mode | 0o111)
        git(root, "config", "--local", "core.hooksPath", ".githooks")
        print("Enabled .githooks for this Git repository (shared by its worktrees). "
              "CI independently checks the complete source tree.")
        return 0
    except (OSError, subprocess.CalledProcessError) as exc:
        print(f"Could not install hooks: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    sys.exit(main())
