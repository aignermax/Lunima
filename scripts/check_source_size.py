#!/usr/bin/env python3
"""Enforce the source ceiling against tracked files or the exact staged blobs."""
import argparse
import json
import pathlib
import subprocess
import sys


POLICY = json.loads(pathlib.Path(__file__).with_name("source_size_policy.json").read_text(encoding="utf-8"))
MAX_LINES = POLICY["max_lines"]
SOURCE_SUFFIXES = frozenset(POLICY["source_suffixes"])
SOURCE_NAMES = frozenset(POLICY["source_names"])
SOURCE_PREFIXES = tuple(POLICY["source_prefixes"])


def git(root, *args):
    return subprocess.run(["git", "-C", str(root), *args], check=True,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout


def is_source(path):
    name = pathlib.PurePosixPath(path)
    return (name.suffix.lower() in SOURCE_SUFFIXES or name.name in SOURCE_NAMES
            or path.startswith(SOURCE_PREFIXES))


def physical_lines(data):
    return data.count(b"\n") + int(bool(data) and not data.endswith(b"\n"))


def checked_sources(root, staged):
    if staged:
        raw = git(root, "diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z")
    else:
        raw = git(root, "ls-files", "-z")
    for encoded in raw.split(b"\0"):
        if not encoded:
            continue
        path = encoded.decode("utf-8")
        if not is_source(path):
            continue
        if staged:
            data = git(root, "show", ":" + path)
        else:
            file = root / path
            if not file.exists():
                continue  # A working-tree deletion adds no oversized source.
            data = file.read_bytes()
        yield path, data


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=pathlib.Path, default=pathlib.Path.cwd())
    parser.add_argument("--staged", action="store_true", help="check staged changes, not editor buffers")
    args = parser.parse_args(argv)
    try:
        root = pathlib.Path(git(args.root, "rev-parse", "--show-toplevel").decode().strip())
        checked = 0
        violations = []
        for path, data in checked_sources(root, args.staged):
            checked += 1
            count = physical_lines(data)
            if b"\0" in data:
                violations.append(f"{path}: binary content in a source file")
            elif count > MAX_LINES:
                violations.append(f"{path}: {count} lines (maximum {MAX_LINES})")
        if violations:
            print("Source size check failed:")
            print("\n".join(violations))
            print("Extract focused responsibilities; partial-only splitting or minifying is not a fix.")
            return 1
        print(f"Source size check passed: {checked} files, maximum {MAX_LINES} lines.")
        return 0
    except (OSError, UnicodeError, subprocess.CalledProcessError) as exc:
        print(f"Source size check could not complete: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    sys.exit(main())
