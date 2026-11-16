#!/usr/bin/env python3
"""Spike #1647: fold the per-runner pilot-journey results into one markdown report.

Usage::

    python3 -I scripts/spikes/summarize_pilot_journeys.py <results-dir>

``<results-dir>`` holds one folder per runner label with the phase results written by
``pilot_journeys.py`` (``1-bare.json``, ``2-nazca.json``, ``3-full.json`` — phases sort by
name) plus the workflow's ``installs.txt`` / ``baseline.txt`` / ``tag.txt`` notes. A cell of
the table names the first phase in which the step passed, i.e. which provisioning it needed.
The report goes to stdout and, on GitHub Actions, to the job summary. Standard library only.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from typing import Dict, List

PASS = "PASS"
NOTES = ("tag.txt", "baseline.txt", "installs.txt")


def load_runs(folder: Path) -> List[dict]:
    """The phase results of one runner, in phase order."""
    return [json.loads(path.read_text(encoding="utf-8")) for path in sorted(folder.glob("*.json"))]


def cell(runs: List[dict], journey: str) -> str:
    """The first phase where ``journey`` passed (with extra environment), else its last verdict."""
    last = "not run"
    for run in runs:
        for outcome in run["outcomes"]:
            if outcome["journey"] != journey:
                continue
            extra = f" + {', '.join(outcome['extra_environment'])}" if outcome["extra_environment"] else ""
            if outcome["status"] == PASS:
                return f"PASS @ {run['phase']}{extra}"
            last = f"{outcome['status']} @ {run['phase']}"
    return last


def table(results: Dict[str, List[dict]]) -> List[str]:
    """Markdown rows: one per journey step, one column per runner label."""
    journeys = sorted({outcome["journey"] for runs in results.values() for run in runs
                       for outcome in run["outcomes"]})
    labels = list(results)
    rows = [f"| step | {' | '.join(labels)} |", f"|---|{'---|' * len(labels)}"]
    rows += [f"| {journey} | {' | '.join(cell(results[label], journey) for label in labels)} |"
             for journey in journeys]
    return rows


def details(label: str, folder: Path, runs: List[dict]) -> List[str]:
    """Everything one runner recorded: notes, then each phase's outcomes and app messages."""
    lines = [f"<details><summary>{label}</summary>", "", "```"]
    for note in NOTES:
        if (folder / note).is_file():
            lines += [f"## {note}", (folder / note).read_text(encoding="utf-8", errors="replace").strip()]
    for run in runs:
        lines.append(f"## phase {run['phase']} ({run['binary']})")
        for outcome in run["outcomes"]:
            lines.append(f"{outcome['status']:<10} {outcome['journey']}: {outcome['detail']}")
            lines += [f"           app said: {message}" for message in outcome["messages"]]
    return lines + ["```", "</details>", ""]


def main(argv: List[str]) -> int:
    """Prints (and on Actions, publishes) the report; returns the process exit code."""
    if len(argv) != 1 or not Path(argv[0]).is_dir():
        print("usage: summarize_pilot_journeys.py <results-dir>", file=sys.stderr)
        return 2
    folders = sorted(path for path in Path(argv[0]).iterdir() if path.is_dir())
    results = {folder.name: load_runs(folder) for folder in folders}
    report = ["# Pilot journeys on the installed release (spike #1647)", ""] + table(results) + [""]
    for folder in folders:
        report += details(folder.name, folder, results[folder.name])
    text = "\n".join(report)
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
