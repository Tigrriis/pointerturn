"""Generate the vehicle icons on the dialogs' vehicle buttons.

Run from the repo root:  python tools/icons/make_vehicle_icons.py   (needs Pillow)

Each vehicle is a simple side view, facing right, drawn from one list of shapes:
  - an SVG, in tools/icons/vehicles (the drawing to edit, or to use elsewhere), and
  - a PNG, in src/PointerPath.BricsCAD/Resources (what the dialogs show: Windows Forms can't
    draw an SVG), four times the size it is shown at, so it stays sharp on a scaled display.

The icons are one colour, the brand ink, with the windows and the gaps round the wheels cut
out, so they read on a button whether it is white or the accent colour. Lengths are in
proportion to the vehicles, though the cars are drawn a little larger than true so they are
not lost beside the trucks: B85 4.91 m, B99 5.2 m, SRV 6.4 m, MRV 8.8 m, HRV 12.5 m.
"""

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SVG_OUT = Path(__file__).resolve().parent / "vehicles"
PNG_OUT = ROOT / "src" / "PointerPath.BricsCAD" / "Resources"

W, H = 152, 52          # the drawing's units; shown at half this (76 x 26) at 100% scaling
GROUND = 50
INK = (0x11, 0x1A, 0x22)
PNG_SCALE = 2           # PNG pixels per unit: 304 x 104
SUPERSAMPLE = 4


class Icon:
    """A vehicle, built up as shapes that are filled (ink) or cut out (clear)."""

    def __init__(self, length):
        self.x0 = (W - length) / 2  # centred
        self.shapes = []            # ("poly", fill, points) or ("circle", fill, cx, cy, r)

    def poly(self, points, fill=True):
        self.shapes.append(("poly", fill, [(self.x0 + x, y) for x, y in points]))
        return self

    def rect(self, x, y, x2, y2, fill=True):
        return self.poly([(x, y), (x2, y), (x2, y2), (x, y2)], fill)

    def circle(self, cx, cy, r, fill=True):
        self.shapes.append(("circle", fill, self.x0 + cx, cy, r))
        return self

    def wheel(self, cx, r):
        """A wheel on the ground: a gap cut out of the body round it, the tyre, and its hub."""
        cy = GROUND - r
        return self.circle(cx, cy, r + 2.5, fill=False).circle(cx, cy, r).circle(cx, cy, r * 0.38, fill=False)

    # ------------------------------------------------------------------ output

    def svg(self, name, title):
        # The cut-outs are a mask: white shows the ink, black clears it. Its id carries the
        # vehicle's name so several icons can go inline on one page.
        def element(shape, colour):
            if shape[0] == "poly":
                return f'<polygon fill="{colour}" points="{" ".join(f"{x:g},{y:g}" for x, y in shape[2])}"/>'
            return f'<circle fill="{colour}" cx="{shape[2]:g}" cy="{shape[3]:g}" r="{shape[4]:g}"/>'

        mask = "\n      ".join(element(s, "#fff" if s[1] else "#000") for s in self.shapes)
        ink = "#%02X%02X%02X" % INK
        return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">
  <title>{title}</title>
  <defs>
    <mask id="vehicle-{name}" maskUnits="userSpaceOnUse" x="0" y="0" width="{W}" height="{H}">
      {mask}
    </mask>
  </defs>
  <rect width="{W}" height="{H}" fill="{ink}" mask="url(#vehicle-{name})"/>
</svg>
'''

    def png(self):
        k = PNG_SCALE * SUPERSAMPLE
        # Coverage first (filled = 255, cut out = 0), then the ink through it.
        cover = Image.new("L", (W * k, H * k), 0)
        d = ImageDraw.Draw(cover)
        for shape in self.shapes:
            value = 255 if shape[1] else 0
            if shape[0] == "poly":
                d.polygon([(x * k, y * k) for x, y in shape[2]], fill=value)
            else:
                _, _, cx, cy, r = shape
                d.ellipse(((cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k), fill=value)
        cover = cover.resize((W * PNG_SCALE, H * PNG_SCALE), Image.LANCZOS)
        img = Image.new("RGBA", cover.size, INK + (0,))
        img.putalpha(cover)
        return img


def car(length, body, windows, wheels, r=7):
    icon = Icon(length).poly(body)
    for w in windows:
        icon.poly(w, fill=False)
    for x in wheels:
        icon.wheel(x, r)
    return icon


def truck(length, box_top, cab_from, cab_top, wheels, r=8):
    """A rigid truck: a box body on a chassis, and a cab at the front with its side window."""
    c, t = cab_from, cab_top
    icon = Icon(length)
    icon.rect(0, box_top, c - 3, 38)                      # the body
    icon.rect(0, 37, length, 42)                          # the chassis, under body and cab
    icon.poly([(c, 42), (c, t), (c + 21, t), (c + 28, t + 11), (c + 31, t + 13), (c + 31, 42)])  # the cab
    icon.poly([(c + 9, t + 3), (c + 20, t + 3), (c + 26, t + 12), (c + 9, t + 12)], fill=False)  # its window
    for x in wheels:
        icon.wheel(x, r)
    return icon


ICONS = {
    # AS/NZS 2890.1 B85, the 85th percentile car: a small hatchback.
    "b85": ("B85 car", car(
        74,
        [(0, 44), (0, 31), (5, 28), (15, 27), (24, 16), (47, 16), (59, 28), (71, 31), (74, 35), (74, 44)],
        [[(26, 19.5), (34, 19.5), (34, 27), (20, 27)], [(37, 19.5), (45.5, 19.5), (53, 27), (37, 27)]],
        wheels=(15, 59), r=7)),
    # AS/NZS 2890.1 B99, the 99.8th percentile car: a large wagon.
    "b99": ("B99 car", car(
        86,
        [(0, 44), (0, 24), (4, 13), (56, 13), (68, 26), (82, 29), (86, 34), (86, 44)],
        [[(8, 16.5), (24, 16.5), (24, 25), (6, 25)], [(27, 16.5), (42, 16.5), (42, 25), (27, 25)], [(45, 16.5), (54, 16.5), (62, 25), (45, 25)]],
        wheels=(17, 68), r=7.5)),
    # AS 2890.2 small, medium and heavy rigid vehicles: 6.4, 8.8 and 12.5 m, the heavy one with a pair of rear axles.
    "srv": ("SRV small rigid vehicle", truck(100, box_top=9, cab_from=69, cab_top=15, wheels=(19, 84), r=7.5)),
    "mrv": ("MRV medium rigid vehicle", truck(122, box_top=5, cab_from=91, cab_top=11, wheels=(26, 106))),
    "hrv": ("HRV heavy rigid vehicle", truck(148, box_top=2, cab_from=117, cab_top=9, wheels=(22, 42, 132))),
}


def main():
    SVG_OUT.mkdir(parents=True, exist_ok=True)
    PNG_OUT.mkdir(parents=True, exist_ok=True)
    for name, (title, icon) in ICONS.items():
        (SVG_OUT / f"{name}.svg").write_text(icon.svg(name, title), encoding="utf-8")
        icon.png().save(PNG_OUT / f"vehicle_{name}.png")
    print(f"wrote {len(ICONS)} SVGs to {SVG_OUT} and {len(ICONS)} PNGs to {PNG_OUT}")


if __name__ == "__main__":
    main()
