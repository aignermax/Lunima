#!/usr/bin/env python3
"""
Build the Lunima wordmark SVGs: the waveguide "L" of the app icon followed by "unima" in
Inter (outlined to paths, so the SVG looks identical without the font installed).

Run scripts/generate_icon.py --brand-dir docs/media/brand first (it writes the mark).
Needs fontTools. Inter (OFL): https://github.com/google/fonts/tree/main/ofl/inter

Usage:
    python3 scripts/generate_wordmark.py <path/to/Inter[opsz,wght].ttf>
"""
import re
import sys
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

FONT = Path(sys.argv[1])
REPO = Path(__file__).resolve().parent.parent
BRAND = REPO / "docs" / "media" / "brand"
WEIGHT = 450
GAP = 80

# Mark geometry (from scripts/generate_icon.py): stroke edges of the L.
STROKE = 88
L_TOP_EDGE = 220 - STROKE / 2
BASELINE = 740 + STROKE / 2
MARK_RIGHT = 800 + 70  # end of the light spot


def text_paths(text: str, x0: float, baseline: float, cap_target: float):
    font = TTFont(FONT)
    font = instantiateVariableFont(font, {"wght": WEIGHT, "opsz": 32})
    cap = font["OS/2"].sCapHeight
    scale = cap_target / cap
    cmap, glyphs, hmtx = font.getBestCmap(), font.getGlyphSet(), font["hmtx"]
    paths, x = [], x0
    for ch in text:
        name = cmap[ord(ch)]
        pen = SVGPathPen(glyphs)
        glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, x, baseline)))
        paths.append(pen.getCommands())
        x += hmtx[name][0] * scale
    d = re.sub(r"-?\d+\.\d+", lambda m: f"{float(m.group()):.1f}".rstrip("0").rstrip("."), " ".join(paths))
    return d, x


def build(fill: str, name: str, spot: str = "#E8FBFF") -> str:
    mark = (BRAND / "lunima-mark.svg").read_text(encoding="utf-8")
    inner = re.search(r"<svg[^>]*>(.*)</svg>", mark, re.S).group(1)
    inner = inner.replace('fill="#E8FBFF"', f'fill="{spot}"')
    d, right = text_paths("unima", MARK_RIGHT + GAP, BASELINE, BASELINE - L_TOP_EDGE)
    pad = 90
    x, y = 360 - STROKE / 2 - pad, L_TOP_EDGE - pad
    w, h = right + pad - x, BASELINE + pad - y
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x:.0f} {y:.0f} {w:.0f} {h:.0f}" '
           f'width="{w:.0f}" height="{h:.0f}" role="img" aria-label="Lunima">'
           f'{inner}<path d="{d}" fill="{fill}"/></svg>\n')
    (BRAND / name).write_text(svg, encoding="utf-8")
    return svg


def main():
    build("#E8ECF4", "lunima-wordmark-dark.svg")                  # for dark backgrounds
    build("#1B2233", "lunima-wordmark-light.svg", spot="#38C6D4")  # for light backgrounds
    print(f"[generate_wordmark] Written: {BRAND}/lunima-wordmark-dark.svg, lunima-wordmark-light.svg")


if __name__ == "__main__":
    main()
