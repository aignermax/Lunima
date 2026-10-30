"""Builds docs/site/circuit.json from Lunima's own example designs (examples/*.lun).

Each design becomes a "tile": its waveguide routes (straight + bend segments from the cached
connection geometry, bends sampled into short polylines) and component anchor points, scaled
so the longest side is TILE_UNITS. Run from the repository root:
    python3 docs/site/build_circuit.py
"""
import json
import math
import os

EXAMPLES = [
    "Length Matching - Fiber Array Fan-In.lun",
    "Mach-Zehnder Interferometer.lun",
    "EBeam Add-Drop Ring.lun",
    "Logic Gate Counter 2-bit.lun",
    "Logic Gate Full Adder.lun",
    "Logic Gate Half Adder.lun",
    "Logic Gate ALU 1-bit.lun",
    "Logic Gate RAM 2x2.lun",
    "Logic Gate Register 2-bit.lun",
]
TILE_UNITS = 1000
ARC_STEP_DEG = 15


def arc_points(seg):
    cx, cy = seg["CenterX"], seg["CenterY"]
    a0 = math.atan2(seg["StartY"] - cy, seg["StartX"] - cx)
    a1 = math.atan2(seg["EndY"] - cy, seg["EndX"] - cx)
    delta = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
    n = max(2, int(abs(math.degrees(delta)) / ARC_STEP_DEG) + 1)
    r = seg["RadiusMicrometers"]
    return [(cx + r * math.cos(a0 + delta * i / n), cy + r * math.sin(a0 + delta * i / n))
            for i in range(1, n + 1)]


def connection_path(conn):
    pts = []
    for seg in conn.get("CachedSegments") or []:
        if not pts:
            pts.append((seg["StartX"], seg["StartY"]))
        if seg["Type"] == "Bend" and "CenterX" in seg:
            pts.extend(arc_points(seg))
        else:
            pts.append((seg["EndX"], seg["EndY"]))
    return pts


def build_tile(design):
    paths = [p for p in map(connection_path, design["Connections"]) if len(p) > 1]
    anchors = [(c["X"], c["Y"]) for c in design["Components"]]
    xs = [x for p in paths for x, _ in p] + [x for x, _ in anchors]
    ys = [y for p in paths for _, y in p] + [y for _, y in anchors]
    x0, y0 = min(xs), min(ys)
    span = max(max(xs) - x0, max(ys) - y0) or 1
    k = TILE_UNITS / span

    def norm(p):
        return [round((p[0] - x0) * k, 1), round((p[1] - y0) * k, 1)]

    return {
        "w": round((max(xs) - x0) * k), "h": round((max(ys) - y0) * k),
        "paths": [[v for q in map(norm, p) for v in q] for p in paths],
        "parts": [norm(a) for a in anchors],
    }


def main():
    tiles = []
    for name in EXAMPLES:
        with open(os.path.join("examples", name), encoding="utf-8") as fh:
            tile = build_tile(json.load(fh))
        tile["name"] = name[:-4]
        tiles.append(tile)
    out = os.path.join("docs", "site", "circuit.json")
    with open(out, "w", encoding="utf-8") as fh:
        json.dump({"source": "examples/*.lun", "tiles": tiles}, fh, separators=(",", ":"))
    print(out, os.path.getsize(out), "bytes,", len(tiles), "tiles")


if __name__ == "__main__":
    main()
