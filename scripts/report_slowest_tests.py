#!/usr/bin/env python3
"""Report the slowest tests/classes from one or more xUnit TRX files.

Usage:
    python3 scripts/report_slowest_tests.py [TRX_OR_DIR ...] [--top N] [--md]

Each argument is a .trx file or a directory that is searched recursively for
.trx files (default: UnitTests/TestResults). Multiple files (e.g. the CI shard
TRXs) are merged; a test appearing in more than one file is counted once with
its longest duration. `--md` prints GitHub-flavoured markdown tables for PR
bodies; the default output is plain text.
"""

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def parse_duration(value):
    """Parse a TRX xs:duration ('[d.]hh:mm:ss[.fffffff]') into seconds."""
    if not value:
        return 0.0
    days = 0.0
    if "." in value.split(":")[0]:  # 'd.hh:mm:ss' form
        day_part, value = value.split(".", 1)
        days = float(day_part) * 86400.0
    parts = value.split(":")
    if len(parts) != 3:
        return days
    hours, minutes, seconds = parts
    return days + int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def load_trx(path):
    """Return (tests, classes) from one TRX file.

    tests: {test_id: (name, class_name, duration_s, outcome)}
    """
    tree = ET.parse(path)
    root = tree.getroot()

    class_by_id = {}
    for unit_test in root.iter(f"{NS}UnitTest"):
        method = unit_test.find(f"{NS}TestMethod")
        class_name = ""
        if method is not None and method.get("className"):
            class_name = method.get("className").split(",")[0].strip()
        class_by_id[unit_test.get("id")] = class_name

    tests = {}
    for result in root.iter(f"{NS}UnitTestResult"):
        test_id = result.get("testId") or result.get("testName")
        name = result.get("testName", "?")
        class_name = class_by_id.get(test_id, "")
        if not class_name and "(" in name:  # theory rows: strip parameter list
            class_name = name.split("(")[0].rsplit(".", 1)[0]
        duration = parse_duration(result.get("duration"))
        outcome = result.get("outcome", "Unknown")
        previous = tests.get(test_id)
        if previous is None or duration > previous[2]:
            tests[test_id] = (name, class_name, duration, outcome)
    return tests


def fmt_duration(seconds):
    if seconds >= 60:
        return f"{int(seconds // 60)}m {seconds % 60:04.1f}s"
    return f"{seconds:.2f}s"


def collect_files(args):
    files = []
    for raw in args:
        path = Path(raw)
        if path.is_dir():
            files.extend(sorted(path.rglob("*.trx")))
        elif path.is_file():
            files.append(path)
        else:
            print(f"warning: {raw} not found", file=sys.stderr)
    return files


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("paths", nargs="*", default=["UnitTests/TestResults"])
    parser.add_argument("--top", type=int, default=25)
    parser.add_argument("--md", action="store_true", help="markdown tables")
    args = parser.parse_args()

    files = collect_files(args.paths)
    if not files:
        print("no .trx files found", file=sys.stderr)
        return 1

    merged = {}
    for trx in files:
        for test_id, entry in load_trx(trx).items():
            previous = merged.get(test_id)
            if previous is None or entry[2] > previous[2]:
                merged[test_id] = entry

    rows = sorted(merged.values(), key=lambda e: e[2], reverse=True)
    total = sum(e[2] for e in merged.values())
    outcomes = {}
    for _, _, _, outcome in merged.values():
        outcomes[outcome] = outcomes.get(outcome, 0) + 1
    counts = ", ".join(f"{k}: {v}" for k, v in sorted(outcomes.items()))

    classes = {}
    for _, class_name, duration, _ in merged.values():
        total_s, count = classes.get(class_name, (0.0, 0))
        classes[class_name] = (total_s + duration, count + 1)
    class_rows = sorted(classes.items(), key=lambda kv: kv[1][0], reverse=True)

    print(f"files: {len(files)} | tests: {len(merged)} ({counts}) | "
          f"sum of test durations: {fmt_duration(total)}")
    print()

    if args.md:
        print(f"| # | Duration | Test |")
        print(f"|---|----------|------|")
        for i, (name, _, duration, _) in enumerate(rows[: args.top], 1):
            print(f"| {i} | {fmt_duration(duration)} | `{name}` |")
        print()
        print("| # | Total | Tests | Class |")
        print("|---|-------|-------|-------|")
        for i, (name, (total_s, count)) in enumerate(class_rows[: args.top], 1):
            print(f"| {i} | {fmt_duration(total_s)} | {count} | `{name or '(unknown)'}` |")
    else:
        print(f"top {args.top} tests:")
        for i, (name, _, duration, _) in enumerate(rows[: args.top], 1):
            print(f"  {i:3}. {fmt_duration(duration):>9}  {name}")
        print()
        print(f"top {args.top} classes:")
        for i, (name, (total_s, count)) in enumerate(class_rows[: args.top], 1):
            print(f"  {i:3}. {fmt_duration(total_s):>9}  ({count:4} tests)  {name or '(unknown)'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
