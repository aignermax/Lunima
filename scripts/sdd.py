"""Run the pilot's versioned acceptance criteria and emit per-criterion evidence."""
import argparse
import hashlib
import json
import os
import platform
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

from run_tests import ROOT, run, test_lock

SPEC = ROOT / "docs/superpowers/specs/sdd-field.json"


def validate(rows):
    if not isinstance(rows, list) or not rows:
        raise ValueError("spec needs criteria")
    ids = set()
    for row in rows:
        if not re.fullmatch(r"[A-Z][A-Z0-9]*-[0-9]+", row["id"]) or row["id"] in ids:
            raise ValueError(f"invalid or duplicate criterion: {row['id']}")
        if not row["behavior"].strip() or not re.fullmatch(r"UnitTests(?:\.\w+){2,}", row["test"]):
            raise ValueError(f"missing behavior or invalid test for {row['id']}")
        ids.add(row["id"])


def check_pin(body, digest):
    pins = re.findall(r"^SDD-Spec:[ \t]*(\S+)[ \t]*$", body.replace("\r", ""), re.M)
    if pins != [digest]:
        raise ValueError(f"expected exactly one line: SDD-Spec: {digest}")


def outcomes(tree, methods):
    tests = {}
    for unit in tree.findall(".//{*}TestDefinitions/{*}UnitTest"):
        method = unit.find("{*}TestMethod")
        if method is not None:
            name = method.get("className", "").split(",")[0] + "." + method.get("name", "")
            tests[unit.get("id")] = name
    found = {}
    for result in tree.findall(".//{*}UnitTestResult"):
        found.setdefault(tests.get(result.get("testId")), []).append(result.get("outcome"))
    return ["missing" if method not in found else
            "passed" if all(value == "Passed" for value in found[method]) else "failed"
            for method in methods]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pin", action="store_true", help="print the canonical spec identifier")
    parser.add_argument("--event", type=Path, help="verify PR description from GitHub event JSON")
    parser.add_argument("--issue-number", type=Path, help="extract the tracking issue from a PR event")
    parser.add_argument("--verify-issue", type=Path, help="compare tracking issue with the contract")
    parser.add_argument("--out", type=Path, default=ROOT / "TestResults/sdd")
    args = parser.parse_args()
    # Git's canonical bytes are independent of Windows checkout line endings.
    content = SPEC.read_bytes().replace(b"\r\n", b"\n")
    digest = hashlib.sha1(b"blob " + str(len(content)).encode() + b"\0" + content).hexdigest()
    rows = json.loads(content)["criteria"]
    validate(rows)
    if args.issue_number:
        pr = json.loads(args.issue_number.read_text(encoding="utf-8"))["pull_request"]
        matches = re.findall(r"^SDD-Issue:[ \t]*([1-9][0-9]*)[ \t]*$", (pr.get("body") or "").replace("\r", ""), re.M)
        if len(matches) != 1:
            raise ValueError("expected one SDD-Issue: <number> line")
        print(matches[0])
        return 0
    if args.verify_issue:
        issue = json.loads(args.verify_issue.read_text(encoding="utf-8"))
        if "pull_request" in issue:
            raise ValueError("tracking reference must identify an issue")
        check_pin(issue.get("body") or "", digest)
        return 0
    if args.pin:
        print(digest)
        return 0
    if args.event:
        pr = json.loads(args.event.read_text(encoding="utf-8"))["pull_request"]
        check_pin(pr.get("body") or "", digest)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if args.event and pr["head"]["sha"] != commit:
        raise ValueError("checked-out commit differs from PR head")
    methods = [row["test"] for row in rows]
    expression = "|".join("FullyQualifiedName=" + method for method in dict.fromkeys(methods))
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=args.out) as directory:
        result = Path(directory).resolve() / "acceptance.trx"
        command = ["dotnet", "test", str(ROOT / "UnitTests/UnitTests.csproj"), "-m:1",
                   "--filter", expression, "--logger", "console;verbosity=minimal",
                   "--logger", "trx;LogFileName=acceptance.trx", "--results-directory", str(result.parent)]
        env = dict(os.environ, DOTNET_PROCESSOR_COUNT="2", DOTNET_CLI_UI_LANGUAGE="en")
        with test_lock():
            code = run(command, env, 1200)
        statuses = outcomes(ET.parse(result).getroot(), methods) if result.exists() else ["missing"] * len(rows)
        if result.exists():
            result.replace(args.out / "acceptance.trx")
    report = dict(commit=commit, spec=digest, environment=platform.platform(), runner_exit=code,
                  criteria=[dict(row, status=status) for row, status in zip(rows, statuses)])
    print(json.dumps(report, indent=2))
    (args.out / "evidence.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return int(code != 0 or any(status != "passed" for status in statuses))


if __name__ == "__main__":
    sys.exit(main())
