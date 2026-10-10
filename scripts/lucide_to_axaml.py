"""Convert Lucide SVG icons (24x24 stroke icons) to an Avalonia ResourceDictionary of
StreamGeometry path data. Usage: python3 -I lucide_to_axaml.py <svg_dir> <out.axaml>"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = "{http://www.w3.org/2000/svg}"


def f(v):
    return float(v)


def fmt(v):
    s = f"{v:.3f}".rstrip("0").rstrip(".")
    return s if s else "0"


def circle(cx, cy, r):
    return (f"M{fmt(cx - r)},{fmt(cy)} a{fmt(r)},{fmt(r)} 0 1,0 {fmt(2 * r)},0 "
            f"a{fmt(r)},{fmt(r)} 0 1,0 {fmt(-2 * r)},0 Z")


def ellipse(cx, cy, rx, ry):
    return (f"M{fmt(cx - rx)},{fmt(cy)} a{fmt(rx)},{fmt(ry)} 0 1,0 {fmt(2 * rx)},0 "
            f"a{fmt(rx)},{fmt(ry)} 0 1,0 {fmt(-2 * rx)},0 Z")


def rect(x, y, w, h, rx, ry):
    if rx == 0 and ry == 0:
        return f"M{fmt(x)},{fmt(y)} h{fmt(w)} v{fmt(h)} h{fmt(-w)} Z"
    rx = min(rx, w / 2)
    ry = min(ry, h / 2)
    return (f"M{fmt(x + rx)},{fmt(y)} H{fmt(x + w - rx)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x + w)},{fmt(y + ry)} "
            f"V{fmt(y + h - ry)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x + w - rx)},{fmt(y + h)} "
            f"H{fmt(x + rx)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x)},{fmt(y + h - ry)} "
            f"V{fmt(y + ry)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x + rx)},{fmt(y)} Z")


def points(pts, close):
    nums = [float(n) for n in re.split(r"[\s,]+", pts.strip()) if n]
    pairs = list(zip(nums[0::2], nums[1::2]))
    d = "M" + " L".join(f"{fmt(x)},{fmt(y)}" for x, y in pairs)
    return d + (" Z" if close else "")


def element_to_path(el):
    tag = el.tag.replace(NS, "")
    a = el.attrib
    if tag == "path":
        # The first moveto of a path is absolute even when written as "m".
        d = a["d"].strip()
        m = re.match(r"^m\s*(-?[\d.]+)[\s,]*(-?[\d.]+)(.*)$", d, re.S)
        if not m:
            return d
        x, y, rest = m.groups()
        # Coordinate pairs right after a moveto are implicit linetos of the same case,
        # so they stay relative.
        rest = rest.strip()
        return f"M{x} {y} l{rest}" if rest[:1] in "-.0123456789" and rest else f"M{x} {y} {rest}"
    if tag == "circle":
        return circle(f(a["cx"]), f(a["cy"]), f(a["r"]))
    if tag == "ellipse":
        return ellipse(f(a["cx"]), f(a["cy"]), f(a["rx"]), f(a["ry"]))
    if tag == "rect":
        rx = f(a.get("rx", a.get("ry", 0)))
        ry = f(a.get("ry", a.get("rx", 0)))
        return rect(f(a.get("x", 0)), f(a.get("y", 0)), f(a["width"]), f(a["height"]), rx, ry)
    if tag == "line":
        return f"M{fmt(f(a['x1']))},{fmt(f(a['y1']))} L{fmt(f(a['x2']))},{fmt(f(a['y2']))}"
    if tag == "polyline":
        return points(a["points"], close=False)
    if tag == "polygon":
        return points(a["points"], close=True)
    raise ValueError(f"unsupported element {tag}")


def pascal(name):
    return "".join(part.capitalize() for part in name.split("-"))


def main(svg_dir, out_path):
    entries = []
    for svg in sorted(Path(svg_dir).glob("*.svg")):
        root = ET.parse(svg).getroot()
        parts = [element_to_path(el) for el in root.iter() if el is not root and el.tag.replace(NS, "") != "title"]
        data = " ".join(parts)
        entries.append((pascal(svg.stem), svg.stem, data))
    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "  <!-- Generated from Lucide icons (lucide-static 0.460.0, ISC license — see",
        "       Assets/Icons/LUCIDE-LICENSE). 24x24 stroke geometry: render with controls:Icon",
        "       (stroked, round caps), never as a filled PathIcon. Regenerate with",
        "       scripts/lucide_to_axaml.py; do not edit by hand. -->",
    ]
    for key, stem, data in entries:
        lines.append(f'  <StreamGeometry x:Key="Icon.{key}">{data}</StreamGeometry>')
    lines.append("</ResourceDictionary>")
    Path(out_path).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{len(entries)} icons -> {out_path}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
