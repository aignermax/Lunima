"""Builds docs/site/circuit.json from Lunima's own example designs (examples/*.lun).

Each design becomes a "tile": its waveguide routes (straight + bend segments from the cached
connection geometry and from the frozen paths inside groups, bends sampled into short
polylines) and component outlines (top-left corner plus the PDK template size), scaled so the
longest side is TILE_UNITS. Run from the repository root:
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
PDKS = [
    os.path.join("CAP-DataAccess", "PDKs", "demo-pdk.json"),
    os.path.join("CAP-DataAccess", "PDKs", "siepic-ebeam-pdk.json"),
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


def segments_path(segments):
    pts = []
    for seg in segments or []:
        if not pts:
            pts.append((seg["StartX"], seg["StartY"]))
        if seg["Type"].lower() == "bend" and "CenterX" in seg:
            pts.extend(arc_points(seg))
        else:
            pts.append((seg["EndX"], seg["EndY"]))
    return pts


SIMPLIFY_TOLERANCE = 2.0  # tile units; tiles are drawn at ~0.3-0.6 px per unit


def _dist_to_segment(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    if dx == 0 and dy == 0:
        return math.hypot(p[0] - a[0], p[1] - a[1])
    t = max(0, min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / (dx * dx + dy * dy)))
    return math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dy)


def simplify(pts, tol=SIMPLIFY_TOLERANCE):
    """Ramer-Douglas-Peucker polyline simplification."""
    if len(pts) < 3:
        return pts
    far, idx = 0, 0
    for i in range(1, len(pts) - 1):
        d = _dist_to_segment(pts[i], pts[0], pts[-1])
        if d > far:
            far, idx = d, i
    if far <= tol:
        return [pts[0], pts[-1]]
    return simplify(pts[:idx + 1], tol)[:-1] + simplify(pts[idx:], tol)


def dedupe(pts):
    out = []
    for p in pts:
        if not out or out[-1] != p:
            out.append(p)
    return out


def load_template_sizes():
    sizes = {}
    for path in PDKS:
        with open(path, encoding="utf-8") as fh:
            for comp in json.load(fh)["components"]:
                sizes[comp["name"]] = (comp["widthMicrometers"], comp["heightMicrometers"])
    return sizes


def all_components(design):
    comps = list(design["Components"])
    for group in design.get("Groups") or []:
        comps.extend(group["ChildComponents"])
    return comps


def all_segment_lists(design):
    lists = [conn.get("CachedSegments") for conn in design["Connections"]]
    for group in design.get("Groups") or []:
        lists.extend(path.get("Segments") for path in group["GroupDto"]["InternalPaths"])
    return lists


def outline(comp, sizes):
    """Axis-aligned body (x, y, w, h); Rotation counts quarter turns, odd turns swap w and h."""
    w, h = sizes[comp["TemplateName"]]
    if comp.get("Rotation", 0) % 2:
        w, h = h, w
    return comp["X"], comp["Y"], w, h


def build_tile(design, sizes):
    paths = [p for p in map(segments_path, all_segment_lists(design)) if len(p) > 1]
    boxes = [outline(c, sizes) for c in all_components(design)]
    corners = [(x, y) for x, y, _, _ in boxes] + [(x + w, y + h) for x, y, w, h in boxes]
    xs = [x for p in paths for x, _ in p] + [x for x, _ in corners]
    ys = [y for p in paths for _, y in p] + [y for _, y in corners]
    x0, y0 = min(xs), min(ys)
    span = max(max(xs) - x0, max(ys) - y0) or 1
    k = TILE_UNITS / span

    def norm(p):
        return [(p[0] - x0) * k, (p[1] - y0) * k]

    def flat(p):
        pts = dedupe([[round(q[0]), round(q[1])] for q in map(norm, p)])
        return [v for q in simplify(pts) for v in q]

    flat_paths = [f for f in map(flat, paths) if len(f) >= 4]

    return {
        "w": round((max(xs) - x0) * k), "h": round((max(ys) - y0) * k),
        "paths": flat_paths,
        "parts": dedupe([[round(v) for v in norm((x, y)) + [w * k, h * k]] for x, y, w, h in boxes]),
    }


def main():
    sizes = load_template_sizes()
    tiles = []
    for name in EXAMPLES:
        with open(os.path.join("examples", name), encoding="utf-8") as fh:
            tile = build_tile(json.load(fh), sizes)
        tiles.append(tile)
    out = os.path.join("docs", "site", "circuit.json")
    with open(out, "w", encoding="utf-8") as fh:
        json.dump({"tiles": tiles}, fh, separators=(",", ":"))
    print(out, os.path.getsize(out), "bytes,", len(tiles), "tiles")


if __name__ == "__main__":
    main()
