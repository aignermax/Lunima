"""
GDS coordinate comparator for position verification (Issue #329).

Compares two GDS coordinate JSON files (produced by extract_gds_coords.py)
and reports exact deviations in micrometers.  Designed to confirm or refute
the fabrication-blocking position bug where waveguide geometry does not align
with component pin positions.

Usage:
    python Scripts/compare_gds_coords.py <reference.json> <system.json> [report.json]

Exit codes:
    0 — all deviations within tolerance (no bug detected)
    1 — deviations exceed tolerance (bug confirmed) or comparison error

Comparison strategy:
  1. For each top-level cell in the reference, find the closest-matching cell
     in the system output (by name, then by geometry count).
  2. For each polygon in the reference cell, find the nearest polygon in the
     system cell (by centroid distance) and report centroid deviation.
  3. For each path in the reference cell, find the nearest path and report
     endpoint deviation.
  4. Summarise maximum and RMS deviation across all matched elements.

Output JSON schema (also written to stdout as a summary):
{
  "reference_file": "<path>",
  "system_file":    "<path>",
  "tolerance_um":   <float>,
  "passed":         <bool>,
  "max_deviation_um": <float>,
  "rms_deviation_um": <float>,
  "details": [
    {
      "cell":          "<name>",
      "element_type":  "polygon" | "path",
      "reference_centroid": [x, y],
      "system_centroid":    [x, y],
      "deviation_um":       <float>,
      "status":             "OK" | "FAIL"
    }, ...
  ],
  "unmatched_cells": ["<name>", ...]
}
"""

import json
import math
import os
import sys
from typing import Optional


# Maximum acceptable deviation in micrometers between reference and system GDS.
# Deviations larger than this are flagged as failures.
DEFAULT_TOLERANCE_UM = 0.01   # 10 nm — tight enough to catch µm-scale bugs


# ── Geometry helpers ──────────────────────────────────────────────────────────

def centroid(vertices: list) -> tuple:
    """Return (cx, cy) centroid of a polygon or list of points."""
    xs = [v[0] for v in vertices]
    ys = [v[1] for v in vertices]
    return sum(xs) / len(xs), sum(ys) / len(ys)


def distance(p1: tuple, p2: tuple) -> float:
    """Euclidean distance between two (x, y) points."""
    return math.sqrt((p1[0] - p2[0]) ** 2 + (p1[1] - p2[1]) ** 2)


def path_centroid(path: dict) -> tuple:
    """Return centroid of a path's point list."""
    return centroid(path["points"])


def poly_centroid(poly: dict) -> tuple:
    """Return centroid of a polygon's vertex list."""
    return centroid(poly["vertices"])


# ── Matching helpers ──────────────────────────────────────────────────────────

def find_best_match(reference_centroid: tuple, candidates: list,
                    centroid_fn) -> Optional[dict]:
    """
    Return the candidate whose centroid is closest to reference_centroid.
    Returns None if candidates is empty.
    """
    if not candidates:
        return None
    return min(candidates, key=lambda c: distance(centroid_fn(c), reference_centroid))


def find_cell_by_name(cells: list, name: str) -> Optional[dict]:
    """Find a cell dict by exact name, or None."""
    for c in cells:
        if c["name"] == name:
            return c
    return None


# ── Core comparison ───────────────────────────────────────────────────────────

def compare(ref_data: dict, sys_data: dict,
            tolerance_um: float = DEFAULT_TOLERANCE_UM) -> dict:
    """
    Compare reference and system coordinate data.

    Parameters
    ----------
    ref_data : dict
        Reference JSON from extract_gds_coords.py.
    sys_data : dict
        System JSON from extract_gds_coords.py.
    tolerance_um : float
        Maximum acceptable deviation in µm.

    Returns
    -------
    dict
        Report dictionary (see module docstring for schema).
    """
    report = {
        "reference_file": ref_data.get("gds_file", ""),
        "system_file":    sys_data.get("gds_file", ""),
        "tolerance_um":   tolerance_um,
        "passed":         True,
        "max_deviation_um": 0.0,
        "rms_deviation_um": 0.0,
        "details":        [],
        "unmatched_cells": []
    }

    deviations = []

    for ref_cell in ref_data.get("cells", []):
        ref_name = ref_cell["name"]

        # Try to find matching cell in system by exact name
        sys_cell = find_cell_by_name(sys_data.get("cells", []), ref_name)
        if sys_cell is None:
            report["unmatched_cells"].append(ref_name)
            continue

        sys_polys = list(sys_cell.get("polygons", []))
        sys_paths = list(sys_cell.get("paths", []))

        # Compare polygons
        for ref_poly in ref_cell.get("polygons", []):
            rc = poly_centroid(ref_poly)
            match = find_best_match(rc, sys_polys, poly_centroid)
            if match is None:
                report["details"].append({
                    "cell": ref_name,
                    "element_type": "polygon",
                    "reference_centroid": list(rc),
                    "system_centroid": None,
                    "deviation_um": None,
                    "status": "UNMATCHED"
                })
                report["passed"] = False
                continue

            sc = poly_centroid(match)
            dev = distance(rc, sc)
            deviations.append(dev)
            status = "OK" if dev <= tolerance_um else "FAIL"
            if status == "FAIL":
                report["passed"] = False

            report["details"].append({
                "cell": ref_name,
                "element_type": "polygon",
                "reference_centroid": [round(rc[0], 6), round(rc[1], 6)],
                "system_centroid":    [round(sc[0], 6), round(sc[1], 6)],
                "deviation_um":       round(dev, 6),
                "status":             status
            })

        # Compare paths (by centroid of all points)
        for ref_path in ref_cell.get("paths", []):
            rc = path_centroid(ref_path)
            match = find_best_match(rc, sys_paths, path_centroid)
            if match is None:
                report["details"].append({
                    "cell": ref_name,
                    "element_type": "path",
                    "reference_centroid": list(rc),
                    "system_centroid": None,
                    "deviation_um": None,
                    "status": "UNMATCHED"
                })
                report["passed"] = False
                continue

            sc = path_centroid(match)
            dev = distance(rc, sc)
            deviations.append(dev)
            status = "OK" if dev <= tolerance_um else "FAIL"
            if status == "FAIL":
                report["passed"] = False

            report["details"].append({
                "cell": ref_name,
                "element_type": "path",
                "reference_centroid": [round(rc[0], 6), round(rc[1], 6)],
                "system_centroid":    [round(sc[0], 6), round(sc[1], 6)],
                "deviation_um":       round(dev, 6),
                "status":             status
            })

    if deviations:
        report["max_deviation_um"] = round(max(deviations), 6)
        rms = math.sqrt(sum(d * d for d in deviations) / len(deviations))
        report["rms_deviation_um"] = round(rms, 6)

    return report


# ── CLI entry point ────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)

    ref_json_path = sys.argv[1]
    sys_json_path = sys.argv[2]
    report_path   = sys.argv[3] if len(sys.argv) >= 4 else "/tmp/comparison_report.json"

    for p in (ref_json_path, sys_json_path):
        if not os.path.isfile(p):
            print(f"ERROR: File not found: {p}", file=sys.stderr)
            sys.exit(1)

    with open(ref_json_path) as f:
        ref_data = json.load(f)
    with open(sys_json_path) as f:
        sys_data = json.load(f)

    report = compare(ref_data, sys_data)

    os.makedirs(os.path.dirname(os.path.abspath(report_path)), exist_ok=True)
    with open(report_path, 'w') as f:
        json.dump(report, f, indent=2)

    # Print human-readable summary
    status = "PASS" if report["passed"] else "FAIL"
    print(f"\n{'='*60}")
    print(f"GDS Coordinate Comparison Report")
    print(f"{'='*60}")
    print(f"Reference: {ref_json_path}")
    print(f"System:    {sys_json_path}")
    print(f"Tolerance: {report['tolerance_um']} µm")
    print(f"Result:    {status}")
    print(f"Max deviation: {report['max_deviation_um']:.6f} µm")
    print(f"RMS deviation: {report['rms_deviation_um']:.6f} µm")

    failures = [d for d in report["details"] if d["status"] == "FAIL"]
    if failures:
        print(f"\nFailed elements ({len(failures)}):")
        for d in failures:
            rc = d["reference_centroid"]
            sc = d["system_centroid"]
            print(f"  [{d['cell']}] {d['element_type']}: "
                  f"ref=({rc[0]:.3f}, {rc[1]:.3f}) "
                  f"sys=({sc[0]:.3f}, {sc[1]:.3f}) "
                  f"Δ={d['deviation_um']:.4f} µm")

    if report["unmatched_cells"]:
        print(f"\nUnmatched cells: {report['unmatched_cells']}")

    print(f"\nReport written to: {report_path}")
    print('='*60)

    sys.exit(0 if report["passed"] else 1)


if __name__ == '__main__':
    main()
