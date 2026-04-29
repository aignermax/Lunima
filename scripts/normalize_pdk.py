#!/usr/bin/env python3
"""One-shot normalizer for the PDK JSON files. Rewrites them in the exact
form the C# PdkJsonSaver would emit, so saver-driven re-saves produce a
zero-line git diff.

Three transformations:
  1. Drop fields whose value is null (matches WhenWritingNull condition).
  2. Reorder PdkComponentDraft keys to the C# DTO declaration order.
  3. Integer-valued floats (e.g. 30.0) serialize as int (matches
     System.Text.Json's default double formatter).
"""
from __future__ import annotations
import json
import sys
from pathlib import Path

# Order from PdkComponentDraft.cs (after the recent reorder).
COMPONENT_KEY_ORDER = [
    "name",
    "category",
    "nazcaFunction",
    "nazcaParameters",
    "widthMicrometers",
    "heightMicrometers",
    "nazcaOriginOffsetX",
    "nazcaOriginOffsetY",
    "pins",
    "sMatrix",
    "sliders",
]
PIN_KEY_ORDER = [
    "name",
    "offsetXMicrometers",
    "offsetYMicrometers",
    "angleDegrees",
    "logicalPinNumber",
]


def reorder(d: dict, key_order: list) -> dict:
    out = {}
    for k in key_order:
        if k in d:
            out[k] = d[k]
    for k, v in d.items():
        if k not in out:
            out[k] = v
    return out


def strip_nulls(obj):
    if isinstance(obj, dict):
        return {k: strip_nulls(v) for k, v in obj.items() if v is not None}
    if isinstance(obj, list):
        return [strip_nulls(v) for v in obj]
    return obj


def reorder_components(obj):
    if isinstance(obj, dict):
        if "pins" in obj and isinstance(obj["pins"], list):
            obj["pins"] = [reorder(p, PIN_KEY_ORDER) if isinstance(p, dict) else p
                           for p in obj["pins"]]
        out = {}
        for k, v in obj.items():
            if k == "components" and isinstance(v, list):
                out[k] = [reorder(reorder_components(c), COMPONENT_KEY_ORDER)
                          if isinstance(c, dict) else c
                          for c in v]
            else:
                out[k] = reorder_components(v)
        return out
    if isinstance(obj, list):
        return [reorder_components(v) for v in obj]
    return obj


def coerce_int_floats(obj):
    if isinstance(obj, dict):
        return {k: coerce_int_floats(v) for k, v in obj.items()}
    if isinstance(obj, list):
        return [coerce_int_floats(v) for v in obj]
    if isinstance(obj, float) and obj.is_integer():
        return int(obj)
    return obj


_UESC_RE = __import__("re").compile(r"\\u([0-9a-f]{4})")
_EXP_RE  = __import__("re").compile(r"(\d)e(-?\d)")


def to_csharp_format(json_str: str) -> str:
    """Massage Python's json output to match System.Text.Json's defaults.

    - Hex digits in \\uXXXX escapes are uppercase in System.Text.Json.
    - Scientific-notation exponent is uppercase E.
    """
    json_str = _UESC_RE.sub(lambda m: r"\u" + m.group(1).upper(), json_str)
    json_str = _EXP_RE.sub(lambda m: f"{m.group(1)}E{m.group(2)}", json_str)
    return json_str


def main():
    if len(sys.argv) < 2:
        print("usage: normalize_pdk.py <pdk.json> [...]", file=sys.stderr)
        sys.exit(1)
    for path_str in sys.argv[1:]:
        path = Path(path_str)
        data = json.loads(path.read_text(encoding="utf-8"))
        data = strip_nulls(data)
        data = reorder_components(data)
        data = coerce_int_floats(data)
        # ensure_ascii=True — System.Text.Json escapes every non-ASCII char
        # by default (no UnsafeRelaxedJsonEscaping in the saver). Match that.
        out = json.dumps(data, indent=2, ensure_ascii=True)
        out = to_csharp_format(out)
        # Force LF line endings — the C# saver writes \n via WriteAllText
        # on every platform. Python's write_text on Windows would translate
        # \n → \r\n by default and cause every line to diff against the
        # saver output.
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(out)
        print(f"normalized {path}")


if __name__ == "__main__":
    main()
