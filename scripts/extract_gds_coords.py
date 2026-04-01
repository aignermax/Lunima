"""
GDS coordinate extractor for position verification (Issue #329).

Reads a GDS file using gdspy and extracts all polygon vertices and path
coordinates, writing them to a JSON file for later comparison.

Usage:
    python scripts/extract_gds_coords.py <input.gds> [output.json]

Output JSON schema:
{
  "gds_file": "<path>",
  "units": {
    "user_unit_m": <float>,     // 1 user unit in metres (e.g. 1e-6 for µm)
    "db_unit_m":   <float>      // 1 database unit in metres
  },
  "cells": [
    {
      "name": "<cell_name>",
      "polygons": [
        {
          "layer": <int>,
          "datatype": <int>,
          "vertices": [[x0, y0], [x1, y1], ...]   // in user units (µm)
        }, ...
      ],
      "paths": [
        {
          "layer": <int>,
          "datatype": <int>,
          "width": <float>,                         // in user units
          "points": [[x0, y0], [x1, y1], ...]      // in user units
        }, ...
      ],
      "refs": [
        {
          "ref_cell": "<name>",
          "origin": [x, y],       // placement origin in user units
          "rotation": <float>,    // degrees
          "magnification": <float>,
          "x_reflection": <bool>
        }, ...
      ]
    }, ...
  ]
}
"""

import json
import os
import sys


def extract_coords(gds_path: str) -> dict:
    """
    Extract all geometric coordinates from a GDS file.

    Parameters
    ----------
    gds_path : str
        Path to the .gds file.

    Returns
    -------
    dict
        Structured coordinate data (see module docstring for schema).
    """
    try:
        import gdspy
    except ImportError:
        raise ImportError(
            "gdspy is required for GDS coordinate extraction.\n"
            "Install with: pip install gdspy"
        )

    lib = gdspy.GdsLibrary(infile=gds_path)

    user_unit = lib.unit          # metres per user unit (typically 1e-6 for µm)
    db_unit = lib.precision       # metres per database unit

    result = {
        "gds_file": os.path.abspath(gds_path),
        "units": {
            "user_unit_m": user_unit,
            "db_unit_m": db_unit,
        },
        "cells": []
    }

    for cell_name, cell in lib.cells.items():
        cell_data = {
            "name": cell_name,
            "polygons": [],
            "paths": [],
            "refs": []
        }

        # Extract polygons
        for poly in cell.polygons:
            # poly.polygons is a list of numpy arrays, one per polygon
            # poly.layers and poly.datatypes are parallel lists
            for i, vertices in enumerate(poly.polygons):
                layer = poly.layers[i] if i < len(poly.layers) else 0
                dtype = poly.datatypes[i] if i < len(poly.datatypes) else 0
                cell_data["polygons"].append({
                    "layer": int(layer),
                    "datatype": int(dtype),
                    "vertices": [[round(float(v[0]), 6), round(float(v[1]), 6)]
                                 for v in vertices]
                })

        # Extract paths
        for path in cell.paths:
            for i, points in enumerate(path.points):
                layer = path.layers[i] if i < len(path.layers) else 0
                dtype = path.datatypes[i] if i < len(path.datatypes) else 0
                width = path.widths[i] if i < len(path.widths) else 0
                cell_data["paths"].append({
                    "layer": int(layer),
                    "datatype": int(dtype),
                    "width": round(float(width), 6),
                    "points": [[round(float(p[0]), 6), round(float(p[1]), 6)]
                               for p in points]
                })

        # Extract cell references (SREFs and AREFs)
        for ref in cell.references:
            ref_name = ref.ref_cell.name if hasattr(ref.ref_cell, 'name') else str(ref.ref_cell)
            origin = ref.origin if ref.origin is not None else [0, 0]
            cell_data["refs"].append({
                "ref_cell": ref_name,
                "origin": [round(float(origin[0]), 6), round(float(origin[1]), 6)],
                "rotation": round(float(ref.rotation or 0), 6),
                "magnification": round(float(ref.magnification or 1), 6),
                "x_reflection": bool(ref.x_reflection)
            })

        result["cells"].append(cell_data)

    return result


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    gds_path = sys.argv[1]
    if not os.path.isfile(gds_path):
        print(f"ERROR: GDS file not found: {gds_path}", file=sys.stderr)
        sys.exit(1)

    json_path = sys.argv[2] if len(sys.argv) >= 3 else os.path.splitext(gds_path)[0] + '_coords.json'

    print(f"Extracting coordinates from: {gds_path}")
    data = extract_coords(gds_path)

    with open(json_path, 'w') as f:
        json.dump(data, f, indent=2)

    n_cells = len(data["cells"])
    n_polys = sum(len(c["polygons"]) for c in data["cells"])
    n_paths = sum(len(c["paths"]) for c in data["cells"])
    n_refs  = sum(len(c["refs"]) for c in data["cells"])

    print(f"Extracted: {n_cells} cells, {n_polys} polygons, {n_paths} paths, {n_refs} refs")
    print(f"Output: {json_path}")
    return json_path


if __name__ == '__main__':
    main()
