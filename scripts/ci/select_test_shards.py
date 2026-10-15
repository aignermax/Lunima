#!/usr/bin/env python3
"""Decide which CI test shards a workflow run needs (test impact selection).

Usage (from the repo root, inside GitHub Actions):
    python3 scripts/ci/select_test_shards.py --event "$GITHUB_EVENT_NAME"
    python3 scripts/ci/select_test_shards.py --event pull_request --changed a.md b.cs

Writes `run`, `mode` and `matrix` to $GITHUB_OUTPUT (stdout when unset) and a
short explanation to $GITHUB_STEP_SUMMARY.

Rules (conservative — when in doubt, run everything):
  * push / schedule / workflow_dispatch -> every shard, Heavy tests included.
  * pull_request -> Heavy tests excluded, and the changed paths decide:
      - docs-only paths (docs/**, root *.md, .claude/**, .github/*.md, issue
        templates, Makefile, .agent.toml, ...) need no tests — unless a test
        source names the file, then everything runs;
      - a path matching a `narrowRules` entry of test-shards.json runs only
        that rule's shards (check_test_shards.py verifies the rule's claim);
      - any other path (code, tests, *.csproj, examples, PDKs, scripts,
        this workflow, ...) runs every shard.
Anything a PR run skips is still caught by the full run on the next push to dev.
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SHARDS_FILE = REPO_ROOT / "scripts" / "ci" / "test-shards.json"
TEST_SOURCE_GLOBS = ("UnitTests/**/*.cs", "UnitTests/**/*.csproj")
FULL_EVENTS = {"push", "schedule", "workflow_dispatch", "repository_dispatch"}

DOCS_ONLY_PATTERNS = [
    r"^docs/",
    r"^\.claude/",
    r"^[^/]+\.md$",
    r"^\.github/[^/]+\.md$",
    r"^\.github/ISSUE_TEMPLATE/",
    r"^\.github/workflows/[^/]+\.disabled$",
    r"^(LICENSE|Makefile|run\.sh|\.agent\.toml|\.gitignore)$",
]


def load_config():
    return json.loads(SHARDS_FILE.read_text(encoding="utf-8"))


def changed_files_from_git():
    """Files changed by the PR: on a pull_request run HEAD is the merge commit,
    so its first parent is the base branch tip."""
    out = subprocess.run(
        ["git", "diff", "--name-only", "--no-renames", "HEAD^1", "HEAD"],
        cwd=REPO_ROOT, capture_output=True, text=True, check=True)
    return [line.strip() for line in out.stdout.splitlines() if line.strip()]


def test_sources_text():
    chunks = []
    for pattern in TEST_SOURCE_GLOBS:
        for path in REPO_ROOT.glob(pattern):
            chunks.append(path.read_text(encoding="utf-8", errors="replace"))
    return "\n".join(chunks)


def is_referenced_by_tests(path, sources):
    """True when a test source spells the file as a string literal — the whole
    path, or the `"parent", "name"` pair a Path.Combine call would use (a bare
    name for root files). Mentions in comments or messages do not count."""
    parts = path.split("/")
    needles = [f'"{path}"']
    if len(parts) >= 2:
        needles.append(f'"{parts[-2]}", "{parts[-1]}"')
    return any(n in sources for n in needles)


def classify(path, config, sources):
    """Returns (shard names | 'all' | 'none', reason)."""
    if any(re.search(p, path) for p in DOCS_ONLY_PATTERNS):
        if is_referenced_by_tests(path, sources):
            return "all", "docs-like file read by a test"
        return "none", "docs-only"
    for rule in config.get("narrowRules", []):
        if re.search(rule["pattern"], path):
            return set(rule["shards"]), "narrow rule: " + rule["why"]
    return "all", "code, tests or build input"


def select(event, changed, config):
    names = [s["name"] for s in config["shards"]]
    if event in FULL_EVENTS:
        return set(names), "full", [f"`{event}` runs the full suite incl. Heavy"]
    if changed is None:
        return set(names), "pr", ["could not compute the diff — running everything"]
    if not changed:
        return set(names), "pr", ["empty diff — running everything"]
    sources = test_sources_text()
    selected, notes = set(), []
    for path in changed:
        result, reason = classify(path, config, sources)
        notes.append(f"`{path}` → {result if isinstance(result, str) else ', '.join(sorted(result))} ({reason})")
        if result == "all":
            selected = set(names)
        elif result != "none":
            selected |= result
    return selected, "pr", notes


def build_matrix(selected, mode, config):
    include = []
    for shard in config["shards"]:
        if shard["name"] not in selected:
            continue
        flt = shard["filter"]
        if mode == "pr":
            flt = f"{flt}&{config['heavyExclusion']}"
        include.append({"name": shard["name"], "filter": flt,
                        "simulationTools": bool(shard.get("simulationTools"))})
    return {"include": include}


def emit(name, value):
    target = os.environ.get("GITHUB_OUTPUT")
    line = f"{name}={value}\n"
    if target:
        with open(target, "a", encoding="utf-8") as fh:
            fh.write(line)
    else:
        sys.stdout.write(line)


def write_summary(selected, mode, notes, config):
    lines = [f"### Test shard selection ({mode})", ""]
    all_names = [s["name"] for s in config["shards"]]
    lines.append("Running: " + (", ".join(n for n in all_names if n in selected) or "**no test shards** (docs-only change)"))
    lines.append("")
    lines.extend(f"- {n}" for n in notes[:200])
    text = "\n".join(lines) + "\n"
    target = os.environ.get("GITHUB_STEP_SUMMARY")
    if target:
        with open(target, "a", encoding="utf-8") as fh:
            fh.write(text)
    print(text)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--event", required=True)
    parser.add_argument("--changed", nargs="*", help="changed paths (default: git diff of the PR merge commit)")
    args = parser.parse_args()
    config = load_config()
    changed = args.changed
    if changed is None and args.event not in FULL_EVENTS:
        try:
            changed = changed_files_from_git()
        except (subprocess.CalledProcessError, OSError) as exc:
            print(f"git diff failed: {exc}", file=sys.stderr)
            changed = None
    selected, mode, notes = select(args.event, changed, config)
    matrix = build_matrix(selected, mode, config)
    write_summary(selected, mode, notes, config)
    emit("run", "true" if matrix["include"] else "false")
    emit("mode", mode)
    emit("matrix", json.dumps(matrix, separators=(",", ":")))


if __name__ == "__main__":
    main()
