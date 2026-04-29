#!/usr/bin/env python3
"""One-shot reconciler: rewrites Lunima PDK JSON entries so their
Width/Height/NazcaOriginOffset/pins match what scripts/render_component_preview.py
actually returns for each component. For pin-count mismatches it
truncates or extends the JSON pin list to the Python pin count and
drops sMatrix connections that reference dropped pin names.

Run once, commit the diff, then delete this script (or keep for the
next PDK addition). Idempotent: running twice produces the same output.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


# Components whose Lunima pin list disagrees with the cell's PinRec —
# these are the "model decisions" the user accepted needed reconciling.
# Map: Lunima component name → action
#   'snap'     — replace Width/Height/origin/pins from Python (preserve names
#                from Lunima where pin counts match)
#   'truncate' — same, but drop excess Lunima pins; rebuild sMatrix
#   'extend'   — same, but add missing pins from Python; preserve sMatrix
#   'clear'    — empty the pins array (component is geometric-only, e.g. BondPad)
PATCHES = {
    # ── 7-pin "GC" variants: cell exposes 1 pin (opt1 chip side), JSON has 2
    "Grating Coupler TE 1550": "truncate",
    "Grating Coupler TE 1310": "truncate",
    "Grating Coupler TE 895":  "truncate",
    "GC SiN TE 1310 8deg":     "truncate",
    "GC SiN TE 1550 8deg":     "truncate",
    "GC TE 1310 8deg":         "truncate",
    "GC TE 1550 8deg":         "truncate",
    "GC TM 1310 8deg":         "truncate",
    "GC TM 1550 8deg":         "truncate",
    # ── SWG splitters: cell exposes 4 pins, JSON has 3
    "SWG Splitter TE 1310":    "extend",
    "SWG Splitter TE 1550":    "extend",
    # ── Bond Pad: cell has no PinRec (only ElecRec on 1/11). Lunima
    # requires every component to declare ≥ 1 pin, so Bond Pad keeps its
    # logical electrical pin. The Check-All report will continue to
    # flag this as NoNazcaPins — that's the honest verdict, the cell
    # genuinely has no optical pin record to align against.
    # "Bond Pad": "clear",  # not applicable (validator-min-pins rule)
    # ── Newly-correct cells from the cell-picking fix
    "Adiabatic Coupler TE 1550": "snap",
    "Adiabatic Coupler TM 1550": "snap",
    "Contra-Directional Coupler": "snap",
    "Disconnected Waveguide TE 1550": "snap",
}


def render(function_name: str, parameters: str) -> dict | None:
    cmd = ["python3", "scripts/render_component_preview.py",
           "siepic_ebeam_pdk", function_name]
    if parameters:
        cmd.append(parameters)
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    if r.returncode != 0:
        return None
    try:
        d = json.loads(r.stdout)
    except json.JSONDecodeError:
        return None
    return d if d.get("success") else None


def lunima_pin_from_nazca(nazca_pin: dict, bbox: dict) -> dict:
    """Convert a Nazca-space pin into a Lunima-space pin dict."""
    return {
        "name": nazca_pin["name"],
        "offsetXMicrometers": nazca_pin["x"] - bbox["xmin"],
        "offsetYMicrometers": bbox["ymax"] - nazca_pin["y"],
        "angleDegrees": int(round(nazca_pin["angle"])),
    }


def reconcile(component: dict, action: str) -> tuple[bool, list[str]]:
    """Mutate `component` in place. Returns (changed, dropped_pin_names)."""
    fn = component.get("nazcaFunction")
    params = component.get("nazcaParameters", "") or ""
    if not fn:
        return False, []
    rendered = render(fn, params)
    if rendered is None:
        return False, []

    bbox = rendered["bbox"]
    component["widthMicrometers"]   = bbox["xmax"] - bbox["xmin"]
    component["heightMicrometers"]  = bbox["ymax"] - bbox["ymin"]
    component["nazcaOriginOffsetX"] = -bbox["xmin"]
    component["nazcaOriginOffsetY"] = -bbox["ymin"]

    if action == "clear":
        dropped = [p["name"] for p in component.get("pins", [])]
        component["pins"] = []
        component.pop("sMatrix", None)
        return True, dropped

    nazca_pins = rendered["pins"]
    lunima_pins = component.get("pins", [])
    new_pins = [lunima_pin_from_nazca(np, bbox) for np in nazca_pins]

    # Preserve Lunima pin names where reasonable: pair Nazca pins with the
    # closest Lunima pin (by post-fix position) and copy the Lunima name.
    dropped: list[str] = []
    if action in ("snap", "truncate", "extend"):
        # Greedy nearest-pair matching to preserve Lunima naming on retained pins.
        kept: list[dict] = []
        unused_lunima = list(lunima_pins)
        for new_p in new_pins:
            if not unused_lunima:
                kept.append(new_p)  # Python-named (opt1 etc.)
                continue
            # Pick the closest Lunima pin by position
            best = min(unused_lunima, key=lambda lp:
                       (lp.get("offsetXMicrometers", 0) - new_p["offsetXMicrometers"]) ** 2 +
                       (lp.get("offsetYMicrometers", 0) - new_p["offsetYMicrometers"]) ** 2)
            unused_lunima.remove(best)
            kept.append({**new_p, "name": best["name"]})
        # Lunima pins that didn't pair with any Nazca pin are dropped.
        dropped = [lp["name"] for lp in unused_lunima]
        new_pins = kept

    component["pins"] = new_pins

    # Drop sMatrix connections that reference dropped pin names.
    smat = component.get("sMatrix")
    if smat and dropped:
        retained_names = {p["name"] for p in component["pins"]}
        old_conns = smat.get("connections", [])
        new_conns = [c for c in old_conns
                     if c.get("fromPin") in retained_names
                     and c.get("toPin")   in retained_names]
        smat["connections"] = new_conns
        # If we dropped to 1 retained pin and the s-matrix is empty, leave a
        # 1-port self-reflection placeholder so simulation doesn't crash.
        if len(retained_names) == 1 and not new_conns:
            only_pin = next(iter(retained_names))
            smat["connections"] = [{
                "fromPin": only_pin, "toPin": only_pin,
                "magnitude": 0.5, "phaseDegrees": 0,
            }]

    return True, dropped


def main():
    if len(sys.argv) != 2:
        print("usage: sync_pdk_to_render.py <pdk.json>", file=sys.stderr)
        sys.exit(1)
    path = Path(sys.argv[1])
    pdk = json.loads(path.read_text(encoding="utf-8"))

    summary = []
    for comp in pdk.get("components", []):
        action = PATCHES.get(comp.get("name"))
        if action is None:
            continue
        changed, dropped = reconcile(comp, action)
        if changed:
            summary.append((comp["name"], action, len(comp["pins"]), dropped))

    path.write_text(json.dumps(pdk, indent=2, ensure_ascii=False), encoding="utf-8")
    for name, action, pin_count, dropped in summary:
        d = f" — dropped: {', '.join(dropped)}" if dropped else ""
        print(f"  [{action:9}] {name}: {pin_count} pin(s){d}")


if __name__ == "__main__":
    main()
