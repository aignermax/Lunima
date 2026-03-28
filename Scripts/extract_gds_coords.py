"""
extract_gds_coords.py

Extracts all polygon and path coordinates from a GDS file using gdspy
and writes them to a structured JSON file for numerical comparison.

Usage:
    python extract_gds_coords.py <input.gds> <output.json>

This tool is part of the GDS debugging workflow for issue #329.
It provides deterministic coordinate data (no AI interpretation, no visual
comparison) to help diagnose coordinate mismatches between component pins
and waveguide placements.
"""

import gdspy
import json
import sys


def extract_coordinates(gds_path):
    """Extract all polygon and path coordinates from a GDS file.

    Args:
        gds_path: Path to the input GDS file.

    Returns:
        Dictionary with database_unit and per-cell polygon/path data.
    """
    lib = gdspy.GdsLibrary(infile=gds_path)

    result = {
        "database_unit": lib.unit,
        "precision": lib.precision,
        "cells": {}
    }

    for cell_name, cell in lib.cells.items():
        cell_data = {
            "polygons": [],
            "paths": []
        }

        # Extract polygons (boundaries in GDS terminology)
        for poly in cell.polygons:
            for i, points in enumerate(poly.polygons):
                cell_data["polygons"].append({
                    "layer": int(poly.layers[i]),
                    "datatype": int(poly.datatypes[i]),
                    "points": points.tolist()
                })

        # Extract paths
        for path in cell.paths:
            for i, points in enumerate(path.polygons):
                cell_data["paths"].append({
                    "layer": int(path.layers[i]),
                    "datatype": int(path.datatypes[i]),
                    "points": points.tolist()
                })

        result["cells"][cell_name] = cell_data

    return result


def main():
    if len(sys.argv) != 3:
        print("Usage: python extract_gds_coords.py <input.gds> <output.json>")
        sys.exit(1)

    gds_path = sys.argv[1]
    output_path = sys.argv[2]

    try:
        coords = extract_coordinates(gds_path)
    except FileNotFoundError:
        print(f"Error: GDS file not found: {gds_path}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Error reading GDS file: {e}", file=sys.stderr)
        sys.exit(1)

    with open(output_path, 'w') as f:
        json.dump(coords, f, indent=2)

    total_polygons = sum(len(c["polygons"]) for c in coords["cells"].values())
    total_paths = sum(len(c["paths"]) for c in coords["cells"].values())
    print(f"Extracted coordinates from {gds_path} to {output_path}")
    print(f"  Cells: {len(coords['cells'])}, Polygons: {total_polygons}, Paths: {total_paths}")
    print(f"  Database unit: {coords['database_unit']} m")


if __name__ == "__main__":
    main()
