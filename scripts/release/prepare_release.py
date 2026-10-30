"""Resolve and validate release source identity before publication."""
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

from scripts.release.sign_manifest import validate_commit, validate_identity, validate_version


def metadata(event_name, event, environment, version, head, tag_commit):
    validate_version(version)
    validate_commit(head)
    if event_name == "repository_dispatch":
        payload = event["client_payload"]
        tag, expected = payload["tag"], payload["sha"]
        if payload["version"] != version:
            raise ValueError("Dispatched version does not match the checked-out project")
    elif event_name == "push" and environment.get("GITHUB_REF_TYPE") == "tag":
        tag, expected = environment["GITHUB_REF_NAME"], environment["GITHUB_SHA"]
    elif event_name == "workflow_dispatch":
        expected = environment["GITHUB_SHA"]
        run = environment["GITHUB_RUN_NUMBER"]
        if head != expected or not re.fullmatch(r"[0-9]+", run):
            raise ValueError("Manual build source identity is invalid")
        return dict(version=version, tag=f"v{version}-build.{run}", commit=head,
                    expected_commit=expected, tag_commit="", stable="false")
    else:
        raise ValueError("Unsupported release trigger")
    validate_identity(version, tag, head, expected, tag_commit)
    return dict(version=version, tag=tag, commit=head, expected_commit=expected,
                tag_commit=tag_commit, stable="true")


def git_commit(reference):
    return subprocess.check_output(["git", "rev-parse", "--verify", reference + "^{commit}"],
                                   text=True).strip()


def main():
    project = ET.parse("CAP.Desktop/CAP.Desktop.csproj")
    versions = [element.text for element in project.iter("Version")]
    if len(versions) != 1:
        raise ValueError("Expected one project version")
    version = versions[0]
    validate_version(version)
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    event_name = os.environ["GITHUB_EVENT_NAME"]
    tag_commit = None if event_name == "workflow_dispatch" else git_commit("refs/tags/v" + version)
    result = metadata(event_name, event, os.environ, version, git_commit("HEAD"), tag_commit)
    with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
        output.write("".join(f"{key}={value}\n" for key, value in result.items()))


if __name__ == "__main__":
    main()
