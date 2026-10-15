"""Encodes one feature-showcase GIF from the frames a Showcase test rendered.

The Category=Showcase tests (UnitTests/UI/Showcase) capture PNG frames of the real
headless MainWindow into a frames directory together with a ``frames.json``:

    {"width": 960, "frames": [{"file": "frame_000.png", "ms": 600}, ...]}

This script turns them into a small, crisp, seamlessly looping GIF:

* every frame gets its own palette (a GIF local colour table): a median cut for the
  bulk plus reserved entries for the pixels it matches badly, so small saturated
  accents (power-flow colours, workspace accents) keep their hue;
* no dithering — the UI is made of flat colours, dithering only adds noise and bytes;
* Pillow's multi-frame writer stores only the changed rectangle of each frame and
  merges identical consecutive frames (adding up their durations).

Usage:
    python assemble_showcase_gif.py <frames_dir> <out.gif>

Requires Pillow. The tests call this automatically when a Python with Pillow is found
(SHOWCASE_PYTHON, a Lunima managed env, or python3/python/py on PATH).
"""
import json
import pathlib
import sys

from PIL import Image, ImageChops
from PIL import GifImagePlugin  # noqa: F401 — explicit so the .gif saver is registered

BASE_COLORS = 192
OUTLIER_COLORS = 63
# Grey-level difference (0..255) above which a pixel counts as badly matched.
OUTLIER_ERROR = 12


def palette_image(colors: list[int]) -> Image.Image:
    holder = Image.new("P", (1, 1))
    holder.putpalette(colors + [0] * (768 - len(colors)))
    return holder


def quantize(image: Image.Image) -> Image.Image:
    """Median cut weights colours by pixel count, so a thin saturated line (a red
    power-flow waveguide, an accent underline) loses its hue. A second pass therefore
    quantizes only the badly matched pixels into the reserved palette entries."""
    base = image.quantize(colors=BASE_COLORS, method=Image.Quantize.MEDIANCUT,
                          dither=Image.Dither.NONE)
    error = ImageChops.difference(image, base.convert("RGB")).convert("L")
    mask = error.point(lambda v: 255 if v > OUTLIER_ERROR else 0)
    rgb, keep = image.tobytes(), mask.tobytes()
    outliers = bytearray()
    for i, flag in enumerate(keep):
        if flag:
            outliers += rgb[3 * i:3 * i + 3]
    colors = base.getpalette()[:3 * BASE_COLORS]
    if outliers:
        strip = Image.frombytes("RGB", (len(outliers) // 3, 1), bytes(outliers))
        extra = strip.quantize(colors=OUTLIER_COLORS, method=Image.Quantize.MEDIANCUT,
                               dither=Image.Dither.NONE)
        colors += extra.getpalette()[:3 * OUTLIER_COLORS]
    return image.quantize(palette=palette_image(colors), dither=Image.Dither.NONE)


def load_frame(path: pathlib.Path, width: int | None) -> Image.Image:
    image = Image.open(path).convert("RGB")
    if width and image.width != width:
        height = round(image.height * width / image.width)
        image = image.resize((width, height), Image.LANCZOS)
    return quantize(image)


def main() -> None:
    frames_dir = pathlib.Path(sys.argv[1])
    out_path = pathlib.Path(sys.argv[2])
    spec = json.loads((frames_dir / "frames.json").read_text(encoding="utf-8"))
    if len(spec["frames"]) < 2:
        raise SystemExit(f"expected at least 2 frames in {frames_dir}")

    frames = [load_frame(frames_dir / e["file"], spec.get("width")) for e in spec["frames"]]
    durations = [int(e["ms"]) for e in spec["frames"]]

    out_path.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(
        out_path,
        save_all=True,
        append_images=frames[1:],
        duration=durations,
        loop=0,
        disposal=1,
    )
    size_kib = out_path.stat().st_size / 1024
    total_s = sum(durations) / 1000
    print(f"{out_path.name}: {len(frames)} frames, {frames[0].width}x{frames[0].height}, "
          f"{total_s:.1f}s loop, {size_kib:.0f} KiB")


if __name__ == "__main__":
    main()
