"""Generate the vehicle icons on the dialogs' vehicle buttons.

Run from the repo root:  python tools/icons/make_vehicle_icons.py   (needs Pillow)

Each vehicle is a simple side view, facing right, drawn from one list of shapes:
  - an SVG, in tools/icons/vehicles (the drawing to edit, or to use elsewhere), and
  - a PNG, in src/PointerPath.BricsCAD/Resources (what the dialogs show: Windows Forms can't
    draw an SVG), four times the size it is shown at, so it stays sharp on a scaled display.

The icons are one colour, the brand ink, with the windows and the gaps round the wheels cut
out, so they read on a button whether it is white or the accent colour.

The two cars are drawn from real vehicles of the design vehicles' sizes, to one scale, so the
B99 stands taller and a little longer than the B85. The trucks are AS 2890.2's rigid vehicles
(6.4, 8.8 and 12.5 m). Each step up is longer than the last, but not to one scale across the
set: the cars are drawn larger than true beside the trucks, so they are not lost.
"""

import math
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


def rounded(points, radius):
    """A polygon with its corners rounded off, as SVG-style path commands.

    radius is one number for every corner, or a list with one for each. A corner is cut back
    along both its edges by its radius (never more than half an edge) and joined by a curve
    that would have passed through the corner: ("M", x, y), ("L", x, y), ("Q", cx, cy, x, y).
    """
    n = len(points)
    radii = radius if isinstance(radius, (list, tuple)) else [radius] * n
    path = []
    for i, (vx, vy) in enumerate(points):
        (px, py), (nx, ny) = points[i - 1], points[(i + 1) % n]
        din, dout = math.hypot(px - vx, py - vy), math.hypot(nx - vx, ny - vy)
        r = min(radii[i], din / 2, dout / 2)
        if r < 0.05:
            path.append(("L" if path else "M", vx, vy))
            continue
        ax, ay = vx + (px - vx) * r / din, vy + (py - vy) * r / din
        bx, by = vx + (nx - vx) * r / dout, vy + (ny - vy) * r / dout
        path.append(("L" if path else "M", ax, ay))
        path.append(("Q", vx, vy, bx, by))
    return path


def flatten(path, steps=12):
    """The path as a polygon, its curves as short straight lines (for Pillow, which has no curves)."""
    points = []
    for cmd in path:
        if cmd[0] in ("M", "L"):
            points.append((cmd[1], cmd[2]))
        else:
            (x0, y0), (_, cx, cy, x1, y1) = points[-1], cmd
            for k in range(1, steps + 1):
                t = k / steps
                points.append(((1 - t) ** 2 * x0 + 2 * (1 - t) * t * cx + t * t * x1,
                               (1 - t) ** 2 * y0 + 2 * (1 - t) * t * cy + t * t * y1))
    return points


class Icon:
    """A vehicle, built up as shapes that are filled (ink) or cut out (clear)."""

    def __init__(self, length):
        self.x0 = (W - length) / 2  # centred
        self.shapes = []            # ("path", fill, commands) or ("circle", fill, cx, cy, r)

    def poly(self, points, fill=True, r=0):
        """A polygon, with its corners rounded by r (one number, or one for each corner)."""
        self.shapes.append(("path", fill, rounded([(self.x0 + x, y) for x, y in points], r)))
        return self

    def rect(self, x, y, x2, y2, fill=True, r=0):
        return self.poly([(x, y), (x2, y), (x2, y2), (x, y2)], fill, r)

    def circle(self, cx, cy, r, fill=True):
        self.shapes.append(("circle", fill, self.x0 + cx, cy, r))
        return self

    def wheel(self, cx, r):
        """A wheel on the ground: a gap cut out of the body round it, the tyre, and its hub."""
        cy = GROUND - r
        return self.circle(cx, cy, r + 2.2, fill=False).circle(cx, cy, r).circle(cx, cy, r * 0.42, fill=False)

    # ------------------------------------------------------------------ output

    def svg(self, name, title):
        # The cut-outs are a mask: white shows the ink, black clears it. Its id carries the
        # vehicle's name so several icons can go inline on one page.
        def element(shape, colour):
            if shape[0] == "path":
                d = " ".join(f"{c[0]}{' '.join(f'{v:.4g}' for v in c[1:])}" for c in shape[2]) + " Z"
                return f'<path fill="{colour}" d="{d}"/>'
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
            if shape[0] == "path":
                d.polygon([(x * k, y * k) for x, y in flatten(shape[2])], fill=value)
            else:
                _, _, cx, cy, r = shape
                d.ellipse(((cx - r) * k, (cy - r) * k, (cx + r) * k, (cy + r) * k), fill=value)
        cover = cover.resize((W * PNG_SCALE, H * PNG_SCALE), Image.LANCZOS)
        img = Image.new("RGBA", cover.size, INK + (0,))
        img.putalpha(cover)
        return img


def car(length, body, body_r, windows, wheels, r, lights=()):
    icon = Icon(length).poly(body, r=body_r)
    for w in windows:
        icon.poly(w, fill=False, r=1.4)
    for light in lights:
        icon.poly(light, fill=False, r=0.8)
    for x in wheels:
        icon.wheel(x, r)
    return icon


def truck(length, box_top, cab_from, cab_top, wheels, r=8):
    """A rigid truck: a box body on a chassis, and a cab at the front with its side window."""
    c, t = cab_from, cab_top
    icon = Icon(length)
    icon.rect(0, box_top, c - 3.5, 38.5, r=2.5)           # the body
    icon.rect(1.5, 36, length - 1, 42.5, r=1.5)           # the chassis, under body and cab
    icon.poly([(c, 42.5), (c, t), (c + 20, t), (c + 28.5, t + 12), (c + 31, t + 14.5), (c + 31, 42.5)],
              r=[1.5, 3, 4, 2, 3, 2.5])                    # the cab
    icon.poly([(c + 8.5, t + 3.5), (c + 18.6, t + 3.5), (c + 24.8, t + 12.4), (c + 8.5, t + 12.4)], fill=False, r=1.4)  # its window
    icon.rect(c + 28.2, 31, c + 31, 34.5, fill=False, r=0.8)  # a headlight
    for x in wheels:
        icon.wheel(x, r)
    return icon


ICONS = {
    # AS/NZS 2890.1 B85, the 85th percentile car: 4.91 x 1.87 m, wheelbase 2.8 m. Drawn from a
    # large sedan of that size, the Ford Falcon (BA): 4.916 x 1.864 m, 1.444 m high, wheelbase
    # 2.829 m. 18 units to the metre: boot, cabin and bonnet, the roof 26 units off the ground.
    "b85": ("B85 car: the size of a Ford Falcon sedan", car(
        88.5,
        [(0, 46), (0, 36.5), (2, 32.5), (17, 32), (32, 24.3), (53, 24), (68, 32.6), (84, 35.5), (88.5, 38.5), (88.5, 46)],
        [2.5, 3, 2.5, 4, 7, 7, 4, 5, 3.5, 2.5],
        [[(22.5, 31.4), (31.5, 26.2), (41.5, 26.2), (41.5, 31.4)], [(44, 26.2), (54, 26.2), (63, 31.4), (44, 31.4)]],
        wheels=(20.8, 71.7), r=6.5,
        lights=[[(85.2, 37.2), (88.5, 39.4), (88.5, 41.2), (84.2, 39)]])),
    # AS/NZS 2890.1 B99, the 99.8th percentile car: 5.2 x 1.94 m, wheelbase 3.05 m. Drawn from a
    # large four-wheel-drive wagon of that size, the Nissan Patrol (Y62): 5.14 to 5.175 x 1.995 m,
    # 1.94 m high, wheelbase 3.075 m. The same 18 units to the metre, so it stands taller and a
    # little longer than the B85. (A long sedan fits as well in plan, the Holden Statesman (WM) at
    # 5.16 x 1.9 m on a 3.009 m wheelbase, but would be hard to tell from the B85 at this size.)
    "b99": ("B99 car: the size of a Nissan Patrol wagon", car(
        93,
        [(0, 45), (0, 31), (2.5, 17), (5, 15), (55, 15), (67, 27.5), (89, 29.5), (93, 32.5), (93, 45)],
        [2.5, 3, 2, 4, 7, 4, 5, 3.5, 2.5],
        [[(6.5, 17.8), (18, 17.8), (18, 26.3), (5.2, 26.3)], [(20.5, 17.8), (36, 17.8), (36, 26.3), (20.5, 26.3)], [(38.5, 17.8), (54.2, 17.8), (62.4, 26.3), (38.5, 26.3)]],
        wheels=(20.9, 76.3), r=7.5,
        lights=[[(89.4, 31.4), (93, 33.6), (93, 36), (88.6, 34)]])),
    # AS 2890.2 small, medium and heavy rigid vehicles: 6.4, 8.8 and 12.5 m, the heavy one with a pair of rear axles.
    "srv": ("SRV small rigid vehicle", truck(100, box_top=9, cab_from=69, cab_top=14.5, wheels=(19, 84), r=7.5)),
    "mrv": ("MRV medium rigid vehicle", truck(122, box_top=5, cab_from=91, cab_top=10.5, wheels=(26, 106))),
    "hrv": ("HRV heavy rigid vehicle", truck(148, box_top=2, cab_from=117, cab_top=8.5, wheels=(22, 42, 132))),
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
