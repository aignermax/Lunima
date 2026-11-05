#!/usr/bin/env python3
"""Refuses unapproved experiment pages under docs/x/.

GitHub Pages publishes docs/ of main, so a page in docs/x/<slug>/ is public once merged.
Product-discovery drafts live in discovery/pages/<slug>/page.json; only the maintainer's
`lunima-team-agent experiment publish` stamps them approved and writes docs/x/<slug>/.
This check makes sure nothing else can get a page there.
"""
import json
import sys
from pathlib import Path


def problems(repo: Path) -> list:
    out = []
    published = repo / "docs" / "x"
    if not published.is_dir():
        return out
    for page_dir in sorted(p for p in published.iterdir()):
        if not page_dir.is_dir():
            out.append(f"docs/x/{page_dir.name}: only <slug>/index.html folders belong here")
            continue
        slug = page_dir.name
        spec_file = repo / "discovery" / "pages" / slug / "page.json"
        if not spec_file.is_file():
            out.append(f"docs/x/{slug}: no discovery/pages/{slug}/page.json — unreviewed page")
            continue
        try:
            spec = json.loads(spec_file.read_text(encoding="utf-8"))
        except ValueError as e:
            out.append(f"discovery/pages/{slug}/page.json: invalid JSON ({e})")
            continue
        if spec.get("status") != "approved" or not str(spec.get("approved_by", "")).strip() or not str(spec.get("approved_on", "")).strip():
            out.append(f"docs/x/{slug}: page spec is not approved (status, approved_by, approved_on)")
        html = page_dir / "index.html"
        if html.is_file() and "PREVIEW — not published" in html.read_text(encoding="utf-8", errors="replace"):
            out.append(f"docs/x/{slug}/index.html: a preview render must not be published")
    return out


def self_test() -> None:
    import tempfile
    with tempfile.TemporaryDirectory() as t:
        repo = Path(t)
        assert problems(repo) == []
        (repo / "docs/x/gds").mkdir(parents=True)
        (repo / "docs/x/gds/index.html").write_text("<html>", encoding="utf-8")
        assert "unreviewed" in problems(repo)[0]
        spec = repo / "discovery/pages/gds/page.json"
        spec.parent.mkdir(parents=True)
        spec.write_text(json.dumps({"status": "draft", "approved_by": ""}), encoding="utf-8")
        assert "not approved" in problems(repo)[0]
        spec.write_text(json.dumps({"status": "approved", "approved_by": "maintainer", "approved_on": "2026-11-20"}), encoding="utf-8")
        assert problems(repo) == []
        (repo / "docs/x/gds/index.html").write_text("PREVIEW — not published", encoding="utf-8")
        assert "preview" in problems(repo)[0]
    print("self-test ok")


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        self_test()
        sys.exit(0)
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(".")
    found = problems(root)
    for p in found:
        print(f"::error::{p}")
    sys.exit(1 if found else 0)
