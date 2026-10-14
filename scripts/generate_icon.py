#!/usr/bin/env python3
"""
Generate the Lunima application icon (LunimaIcon.ico and LunimaIcon.icns).

The icon is the letter "L" drawn as a single glowing waveguide: a straight run,
one rounded bend, and a bright light spot where the light leaves the guide. It sits
on the app's dark surface and uses the Optics (cyan) and Logic (violet) workspace
accents. Small sizes (<= 32 px) use a simplified, bolder drawing so the L stays
legible in a taskbar or a file list.

The same geometry is written as SVG for the website and README with --brand-dir.

Usage:
    python3 scripts/generate_icon.py [--output Installer/LunimaIcon.ico]
                                     [--icns Installer/LunimaIcon.icns]
                                     [--brand-dir docs/media/brand]
"""

import argparse
import math
import platform
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


# ---------------------------------------------------------------------------
# Geometry (1024 x 1024 design grid)
# ---------------------------------------------------------------------------
GRID = 1024
TILE_INSET, TILE_RADIUS = 64, 200             # rounded-square background
SMALL_TILE_INSET, SMALL_TILE_RADIUS = 24, 220 # small sizes fill more of the cell
L_TOP = (360, 220)                            # start of the vertical run
BEND_RADIUS = 180
L_END = (800, 740)                            # light spot at the end of the run
STROKE, SMALL_STROKE = 88, 132
HIGHLIGHT_STROKE = 18
SPOT_RADIUS, SMALL_SPOT_RADIUS = 70, 92
GLOW_BLUR = 22
SMALL_SIZE_LIMIT = 32

# ---------------------------------------------------------------------------
# Colours (match CAP.Avalonia/Styles/Tokens.axaml + workspace accents)
# ---------------------------------------------------------------------------
TILE_TOP, TILE_BOTTOM = (27, 34, 51), (13, 17, 26)
TILE_EDGE = (255, 255, 255, 20)
LOGIC_VIOLET = (176, 146, 247)
OPTICS_CYAN = (56, 198, 212)
SPOT = (232, 251, 255)
HIGHLIGHT = (255, 255, 255, 140)


def _l_points(steps: int = 90) -> list[tuple[float, float]]:
    """Centre line of the L: down, a quarter-circle bend, then right."""
    x0, y0 = L_TOP
    bend_y = L_END[1] - BEND_RADIUS
    cx, cy = x0 + BEND_RADIUS, bend_y
    pts = [(x0, y0), (x0, bend_y)]
    for i in range(1, steps):
        a = math.pi - (math.pi / 2) * i / steps
        pts.append((cx + BEND_RADIUS * math.cos(a), cy + BEND_RADIUS * math.sin(a)))
    pts += [(cx, L_END[1]), L_END]
    return pts


def _scaled(pts, s: float):
    return [(x * s, y * s) for x, y in pts]


def _dense(pts, step: float):
    """Resample a polyline so consecutive points are at most *step* apart."""
    out = [pts[0]]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(1, math.ceil(math.hypot(x1 - x0, y1 - y0) / step))
        out += [(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n) for i in range(1, n + 1)]
    return out


def _stroke_mask(size: int, width: float) -> Image.Image:
    """Alpha mask of the L waveguide: discs stamped along the centre line give a
    perfectly round bend and round caps (PIL's wide-line joints facet on curves)."""
    s = size / GRID
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    r = max(0.5, width * s / 2)
    for x, y in _dense(_scaled(_l_points(), s), step=max(0.5, r / 8)):
        draw.ellipse((x - r, y - r, x + r, y + r), fill=255)
    return mask


def _vertical_gradient(size: int, top, bottom, y_from: float = 0.0, y_to: float | None = None) -> Image.Image:
    """Gradient from *top* at y_from to *bottom* at y_to (clamped outside)."""
    y_to = size - 1 if y_to is None else y_to
    column = Image.new("RGBA", (1, size))
    for y in range(size):
        t = min(1.0, max(0.0, (y - y_from) / max(1.0, y_to - y_from)))
        column.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    return column.resize((size, size))


def _tile(size: int, small: bool) -> Image.Image:
    inset = (SMALL_TILE_INSET if small else TILE_INSET) * size / GRID
    radius = (SMALL_TILE_RADIUS if small else TILE_RADIUS) * size / GRID
    box = (inset, inset, size - inset, size - inset)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    img.paste(_vertical_gradient(size, TILE_TOP, TILE_BOTTOM), mask=mask)
    if not small:
        ImageDraw.Draw(img).rounded_rectangle(box, radius=radius, outline=TILE_EDGE, width=max(1, size // 170))
    return img


def _light(size: int) -> Image.Image:
    """Violet-to-cyan light along the guide: violet at the top, cyan at the exit."""
    s = size / GRID
    return _vertical_gradient(size, LOGIC_VIOLET, OPTICS_CYAN, L_TOP[1] * s, L_END[1] * s)


def _spot(img: Image.Image, radius: float) -> None:
    s = img.size[0] / GRID
    x, y = L_END[0] * s, L_END[1] * s
    r = radius * s
    ImageDraw.Draw(img).ellipse((x - r, y - r, x + r, y + r), fill=SPOT + (255,))


def _render_full(size: int) -> Image.Image:
    img = _tile(size, small=False)
    light = _light(size)
    glow = _stroke_mask(size, STROKE + 8).filter(ImageFilter.GaussianBlur(GLOW_BLUR * size / GRID))
    img.paste(light, mask=glow.point(lambda a: a * 0.8))
    img.paste(light, mask=_stroke_mask(size, STROKE))
    core = Image.new("RGBA", (size, size), HIGHLIGHT)
    img.alpha_composite(Image.composite(core, Image.new("RGBA", (size, size)), _stroke_mask(size, HIGHLIGHT_STROKE)))
    halo = Image.new("L", (size, size), 0)
    s = size / GRID
    r = SPOT_RADIUS * 1.8 * s
    ImageDraw.Draw(halo).ellipse((L_END[0] * s - r, L_END[1] * s - r, L_END[0] * s + r, L_END[1] * s + r), fill=110)
    img.paste(Image.new("RGBA", (size, size), SPOT + (255,)), mask=halo.filter(ImageFilter.GaussianBlur(40 * s)))
    _spot(img, SPOT_RADIUS)
    return img


def _render_small(size: int) -> Image.Image:
    img = _tile(size, small=True)
    img.paste(_light(size), mask=_stroke_mask(size, SMALL_STROKE))
    _spot(img, SMALL_SPOT_RADIUS)
    return img


def render_icon(size: int) -> Image.Image:
    """Render the icon at *size* px, supersampled 4x for clean edges."""
    draw_size = max(size * 4, 256)
    big = _render_small(draw_size) if size <= SMALL_SIZE_LIMIT else _render_full(draw_size)
    return big.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------------------
# SVG (website / README)
# ---------------------------------------------------------------------------
def _l_path_data() -> str:
    x0, y0 = L_TOP
    bend_y = L_END[1] - BEND_RADIUS
    return (f"M{x0} {y0} V{bend_y} A{BEND_RADIUS} {BEND_RADIUS} 0 0 0 "
            f"{x0 + BEND_RADIUS} {L_END[1]} H{L_END[0]}")


def _svg_defs() -> str:
    return (
        '<defs><linearGradient id="lum-light" gradientUnits="userSpaceOnUse" '
        f'x1="0" y1="{L_TOP[1]}" x2="0" y2="{L_END[1]}">'
        '<stop offset="0" stop-color="#B092F7"/><stop offset="1" stop-color="#38C6D4"/></linearGradient>'
        '<filter id="lum-glow" x="-30%" y="-30%" width="160%" height="160%">'
        f'<feGaussianBlur stdDeviation="{GLOW_BLUR}"/></filter></defs>'
    )


def _svg_mark_body() -> str:
    d = _l_path_data()
    return (
        f'<path d="{d}" stroke="url(#lum-light)" stroke-width="{STROKE + 8}" fill="none" '
        'stroke-linecap="round" filter="url(#lum-glow)" opacity="0.8"/>'
        f'<path d="{d}" stroke="url(#lum-light)" stroke-width="{STROKE}" fill="none" stroke-linecap="round"/>'
        f'<path d="{d}" stroke="#FFFFFF" stroke-opacity="0.55" stroke-width="{HIGHLIGHT_STROKE}" '
        'fill="none" stroke-linecap="round"/>'
        f'<circle cx="{L_END[0]}" cy="{L_END[1]}" r="{SPOT_RADIUS}" fill="#E8FBFF"/>'
    )


def build_svgs(brand_dir: Path) -> None:
    """Write lunima-icon.svg (with tile) and lunima-mark.svg (the L alone, transparent)."""
    brand_dir.mkdir(parents=True, exist_ok=True)
    tile = (
        '<defs><linearGradient id="lum-tile" x1="0" y1="0" x2="0" y2="1">'
        '<stop offset="0" stop-color="#1B2233"/><stop offset="1" stop-color="#0D111A"/></linearGradient></defs>'
        f'<rect x="{TILE_INSET}" y="{TILE_INSET}" width="{GRID - 2 * TILE_INSET}" height="{GRID - 2 * TILE_INSET}" '
        f'rx="{TILE_RADIUS}" fill="url(#lum-tile)" stroke="#FFFFFF" stroke-opacity="0.08" stroke-width="6"/>'
    )
    head = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {GRID} {GRID}" width="{GRID}" height="{GRID}">'
    (brand_dir / "lunima-icon.svg").write_text(head + tile + _svg_defs() + _svg_mark_body() + "</svg>\n", encoding="utf-8")
    # The mark is cropped to the L's bounds plus glow room so it can sit inline next to text.
    pad = STROKE
    x, y = L_TOP[0] - pad, L_TOP[1] - pad
    w, h = L_END[0] + SPOT_RADIUS + pad - x, L_END[1] + SPOT_RADIUS + pad - y
    mark_head = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x} {y} {w} {h}" width="{w}" height="{h}">'
    (brand_dir / "lunima-mark.svg").write_text(mark_head + _svg_defs() + _svg_mark_body() + "</svg>\n", encoding="utf-8")
    print(f"[generate_icon] Written: {brand_dir}/lunima-icon.svg, lunima-mark.svg")


# ---------------------------------------------------------------------------
# ICO / ICNS / PNG
# ---------------------------------------------------------------------------
def build_ico(output_path: Path) -> None:
    """Build a multi-resolution ICO with every size rendered individually (small sizes simplified)."""
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [render_icon(sz) for sz in sizes]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    images[-1].save(output_path, format="ICO", sizes=[(sz, sz) for sz in sizes], append_images=images[:-1])
    print(f"[generate_icon] Written: {output_path}  ({len(sizes)} sizes: {sizes})")


def build_png(output_path: Path, size: int) -> None:
    """Write a single PNG (window icon resource)."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    render_icon(size).save(output_path, format="PNG")
    print(f"[generate_icon] Written: {output_path}  ({size}px)")


def build_icns(output_path: Path) -> None:
    """Build a macOS ICNS file: iconutil on macOS, Pillow's encoder elsewhere."""
    iconset_sizes = [16, 32, 64, 128, 256, 512, 1024]
    output_path.parent.mkdir(parents=True, exist_ok=True)

    if platform.system() == "Darwin":
        try:
            with tempfile.TemporaryDirectory() as tmpdir:
                iconset_dir = Path(tmpdir) / "LunimaIcon.iconset"
                iconset_dir.mkdir(parents=True, exist_ok=True)
                for size in iconset_sizes:
                    render_icon(size).save(iconset_dir / f"icon_{size}x{size}.png", format="PNG")
                subprocess.run(
                    ["iconutil", "-c", "icns", "-o", str(output_path), str(iconset_dir)],
                    check=True,
                    capture_output=True,
                )
                print(f"[generate_icon] Written: {output_path}  ({len(iconset_sizes)} sizes: {iconset_sizes})")
                return
        except (FileNotFoundError, subprocess.CalledProcessError) as e:
            print(f"[generate_icon] Warning: iconutil failed, falling back to Pillow: {e}")

    try:
        render_icon(1024).save(output_path, format="ICNS")
        print(f"[generate_icon] Written: {output_path}  (Pillow ICNS fallback, {len(iconset_sizes)} sizes: {iconset_sizes})")
    except Exception as e:
        print(f"[generate_icon] Error: Unable to generate ICNS file: {e}")
        raise


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate the Lunima application icons (ICO, ICNS, SVG)")
    parser.add_argument("--output", default="Installer/LunimaIcon.ico",
                        help="Output path for the .ico file (default: Installer/LunimaIcon.ico)")
    parser.add_argument("--icns", default=None,
                        help="Output path for the .icns file (default: Installer/LunimaIcon.icns)")
    parser.add_argument("--png", default=None, help="Optional 256 px PNG output path (window icon)")
    parser.add_argument("--brand-dir", default=None, help="Optional directory for the SVG icon and mark")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    build_ico((repo_root / args.output).resolve())
    build_icns((repo_root / (args.icns or "Installer/LunimaIcon.icns")).resolve())
    if args.png:
        build_png((repo_root / args.png).resolve(), 256)
    if args.brand_dir:
        build_svgs((repo_root / args.brand_dir).resolve())


if __name__ == "__main__":
    main()
