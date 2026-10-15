#!/usr/bin/env python3
"""Prove that the CI shard filters in test-shards.json partition the test suite.

Usage (UnitTests must already be built):
    python3 scripts/ci/check_test_shards.py [--project UnitTests/UnitTests.csproj]

For both modes it lists the tests each shard filter selects
(`dotnet test --no-build --list-tests --filter ...`) and compares the multiset
union with the unfiltered list:
  * full mode (push / nightly / release): shards together == every test;
  * pr mode (Heavy excluded): shards together == every test with Category!=Heavy.
A test missing from all shards, or selected by two shards, fails the check.
It also verifies each narrowRules entry: every test class whose source matches
the rule's readerPattern must run entirely inside the rule's shards.
Exit code 0 = partition holds.
"""

import argparse
import collections
import json
import os
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SHARDS_FILE = REPO_ROOT / "scripts" / "ci" / "test-shards.json"
TEST_LINE = re.compile(r"^    (\S.*?)\s*$")


def list_tests(project, test_filter):
    cmd = ["dotnet", "test", project, "--no-build", "--list-tests"]
    if test_filter:
        cmd += ["--filter", test_filter]
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en", DOTNET_NOLOGO="1")
    out = subprocess.run(cmd, cwd=REPO_ROOT, capture_output=True, text=True, env=env)
    if out.returncode != 0:
        sys.exit(f"listing failed for filter {test_filter!r}:\n{out.stdout}\n{out.stderr}")
    names = [m.group(1) for m in map(TEST_LINE.match, out.stdout.splitlines()) if m]
    return collections.Counter(names)


def check_mode(label, shard_lists, expected):
    union = collections.Counter()
    owners = collections.defaultdict(list)
    for name, tests in shard_lists.items():
        union.update(tests)
        for test in tests:
            owners[test].append(name)
    missing = expected - union
    duplicated = {t: owners[t] for t in union if union[t] > expected.get(t, 0) and t in expected}
    unexpected = union - expected
    print(f"[{label}] expected {sum(expected.values())} tests, shards select {sum(union.values())}")
    for name, tests in shard_lists.items():
        print(f"    {name:40s} {sum(tests.values()):6d}")
    ok = True
    if missing:
        ok = False
        print(f"  MISSING from every shard ({sum(missing.values())}):")
        for t in sorted(missing)[:50]:
            print(f"    {t}")
    if duplicated:
        ok = False
        print(f"  SELECTED BY MORE THAN ONE SHARD ({len(duplicated)}):")
        for t in sorted(duplicated)[:50]:
            print(f"    {t}  <- {', '.join(duplicated[t])}")
    if unexpected and not duplicated:
        ok = False
        print(f"  SELECTED BUT NOT EXPECTED ({sum(unexpected.values())}):")
        for t in sorted(unexpected)[:50]:
            print(f"    {t}")
    return ok


def classes_in(source):
    ns = re.search(r"^\s*namespace\s+([\w.]+)", source, re.M)
    prefix = ns.group(1) + "." if ns else ""
    return [prefix + c for c in re.findall(r"\bclass\s+(\w+)", source)]


def check_narrow_rules(config, full_lists):
    ok = True
    for rule in config.get("narrowRules", []):
        allowed = set(rule["shards"])
        reader = re.compile(rule["readerPattern"])
        for path in (REPO_ROOT / "UnitTests").rglob("*.cs"):
            source = path.read_text(encoding="utf-8", errors="replace")
            if not reader.search(source):
                continue
            for cls in classes_in(source):
                for shard, tests in full_lists.items():
                    hits = [t for t in tests if t.startswith(cls + ".")]
                    if hits and shard not in allowed:
                        ok = False
                        print(f"  narrow rule {rule['pattern']!r}: {cls} reads the changed files "
                              f"but runs in {shard} (rule allows {sorted(allowed)})")
    print(f"[narrow rules] {'ok' if ok else 'VIOLATED'}")
    return ok


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--project", default="UnitTests/UnitTests.csproj")
    args = parser.parse_args()
    config = json.loads(SHARDS_FILE.read_text(encoding="utf-8"))
    heavy = config["heavyExclusion"]

    full_lists = {s["name"]: list_tests(args.project, s["filter"]) for s in config["shards"]}
    pr_lists = {s["name"]: list_tests(args.project, f"{s['filter']}&{heavy}") for s in config["shards"]}
    everything = list_tests(args.project, None)
    without_heavy = list_tests(args.project, heavy)

    ok = check_mode("full", full_lists, everything)
    ok = check_mode("pr", pr_lists, without_heavy) and ok
    heavy_count = sum(everything.values()) - sum(without_heavy.values())
    print(f"[heavy] {heavy_count} test cases are Heavy (skipped on pull requests)")
    ok = check_narrow_rules(config, full_lists) and ok
    print("PARTITION OK" if ok else "PARTITION BROKEN — fix scripts/ci/test-shards.json")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
