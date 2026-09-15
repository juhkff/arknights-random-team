"""Render the application icon set from the app's own brand mark.

The mark is the cut-corner square with the "RT" monogram that the sidebar shows in
its top-left corner (see MainWindow.axaml, the 42x42 Path + TextBlock block), redrawn
here as a filled mint tile so it still reads at 16 px.

Every size is rendered natively (geometry and font size scale with the canvas), so
small sizes stay crisp instead of being downscaled from a large master.

Usage (from the repository root):
    python3 tools/gen_mark_icon.py

Writes Assets/icon-{16,24,32,48,64,128,256,512}.png and Assets/app.ico.
app.ico feeds <ApplicationIcon> (the EXE icon); Assets/icon-256.png is embedded as an
AvaloniaResource and used as the window icon.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent.parent / "Assets"
FONT_CANDIDATES = [
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
    "C:/Windows/Fonts/arialbd.ttf",
    "C:/Windows/Fonts/segoeuib.ttf",
]

CUT_RATIO = 30 / 42          # corner cut, from the sidebar mark (32/42) eased a touch
MARK_RATIO = 0.56            # mark span relative to the canvas
RADIUS_RATIO = 0.184         # tile corner radius
LETTER_RATIO = 0.54          # monogram width relative to the mark

TILE_TOP, TILE_BOTTOM = (0x16, 0x24, 0x30), (0x08, 0x0F, 0x16)
MINT = (0x65, 0xE6, 0xC4)
DARK = (0x0D, 0x18, 0x22)
ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)
ALL_SIZES = ICO_SIZES + (512,)


def font_path():
    for candidate in FONT_CANDIDATES:
        if Path(candidate).exists():
            return candidate
    raise SystemExit("no bold sans font found; add one to FONT_CANDIDATES")


def vertical_tile(size, radius, top, bottom):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    grad = Image.new("RGBA", (size, size))
    d = ImageDraw.Draw(grad)
    for y in range(size):
        t = y / max(size - 1, 1)
        d.line([(0, y), (size, y)],
               fill=tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    img.alpha_composite(Image.composite(grad, Image.new("RGBA", (size, size), (0, 0, 0, 0)), mask))
    return img


def render(size):
    img = vertical_tile(size, max(2, round(size * RADIUS_RATIO)), TILE_TOP, TILE_BOTTOM)
    d = ImageDraw.Draw(img)

    mark = round(size * MARK_RATIO)
    ox = oy = (size - mark) // 2
    cut = round(mark * CUT_RATIO)

    # filled cut-corner square: top-right corner sliced on the diagonal
    d.polygon([(ox, oy), (ox + mark - cut, oy), (ox + mark, oy + cut),
               (ox + mark, oy + mark), (ox, oy + mark)], fill=MINT)

    # monogram, sized by real font metrics and centred in the band left of the cut
    target = mark * LETTER_RATIO
    pt = max(6, round(size * 0.05))
    while pt < size:
        f = ImageFont.truetype(font_path(), pt)
        b = d.textbbox((0, 0), "RT", font=f)
        if b[2] - b[0] >= target:
            break
        pt += 1
    f = ImageFont.truetype(font_path(), pt)
    b = d.textbbox((0, 0), "RT", font=f)
    tw, th = b[2] - b[0], b[3] - b[1]
    band_left = ox + mark * 0.035
    band_right = ox + mark * 0.80
    d.text((band_left + (band_right - band_left - tw) / 2 - b[0],
            oy + (mark - th) / 2 - b[1] + mark * 0.055), "RT", font=f, fill=DARK)
    return img


def ico(entries):
    import struct
    head = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    dirs, blobs = b"", []
    for size, data in entries:
        w = 0 if size >= 256 else size
        dirs += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs.append(data)
    return head + dirs + b"".join(blobs)


def main():
    import io
    OUT.mkdir(parents=True, exist_ok=True)
    pngs = {}
    for size in ALL_SIZES:
        img = render(size)
        buf = io.BytesIO()
        img.save(buf, format="PNG", optimize=True)
        pngs[size] = buf.getvalue()
        (OUT / f"icon-{size}.png").write_bytes(pngs[size])
    (OUT / "app.ico").write_bytes(ico([(s, pngs[s]) for s in ICO_SIZES]))
    print("wrote:", ", ".join(f"icon-{s}.png" for s in ALL_SIZES), "and app.ico")


if __name__ == "__main__":
    main()
