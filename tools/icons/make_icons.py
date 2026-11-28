"""Generate the ribbon icons in src/PointerPath.BricsCAD/Resources.

Run from the repo root:  python tools/icons/make_icons.py   (needs Pillow)

Each icon is drawn twice, on a transparent canvas several times its size and then downsampled:
once for the 32 px (large button) PNG, and once, with heavier lines and less detail, for the
16 px (small button) PNG, which a scaled-down large icon would leave a smudge.

The look: the vehicle in plan, in the brand's cyan with a darker edge; what it does (its path,
its swept area) in a warm red; kerbs and boundaries in grey. All are mid-tones with their own
edges, so they read on a light ribbon and on a dark one.
"""

import math
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[2] / "src" / "PointerPath.BricsCAD" / "Resources"
S = 256  # drawing canvas; 8x the large size

CYAN = (63, 220, 255, 255)        # the brand accent: the vehicle
CYAN_EDGE = (10, 122, 158, 255)   # its outline, glass and tyres
RED = (236, 82, 72, 255)          # what the vehicle does: its path
RED_EDGE = (176, 44, 38, 255)
RED_SOFT = (236, 82, 72, 105)     # its swept area
GREY = (126, 138, 150, 255)       # kerbs, walls, boundaries
GOLD = (245, 186, 54, 255)
GOLD_EDGE = (150, 104, 14, 255)
ORANGE = (240, 138, 76, 255)
ORANGE_EDGE = (158, 76, 30, 255)
EARTH = (150, 120, 92, 255)
EARTH_EDGE = (98, 74, 52, 255)
WHITE = (255, 255, 255, 255)


def canvas():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def stroke(d, points, width, fill):
    """A line through the points with round joins and round ends."""
    d.line(points, fill=fill, width=width, joint="curve")
    r = width / 2
    for x, y in (points[0], points[-1]):
        d.ellipse((x - r, y - r, x + r, y + r), fill=fill)


def bezier(p0, p1, p2, p3, steps=40):
    """Points along a cubic curve."""
    points = []
    for i in range(steps + 1):
        t = i / steps
        a, b, c, e = (1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3
        points.append((a * p0[0] + b * p1[0] + c * p2[0] + e * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + e * p3[1]))
    return points


def arc(cx, cy, r, start, end, steps=40):
    """Points along a circular arc, angles in degrees (0 to the right, 90 down, as Pillow has them)."""
    return [(cx + r * math.cos(math.radians(start + (end - start) * i / steps)),
             cy + r * math.sin(math.radians(start + (end - start) * i / steps))) for i in range(steps + 1)]


def arrow_head(d, tip, towards, size, fill):
    """A triangular head with its point at tip, pointing the way (towards -> tip) runs."""
    ang = math.atan2(tip[1] - towards[1], tip[0] - towards[0])
    back = (tip[0] - size * math.cos(ang), tip[1] - size * math.sin(ang))
    half = size * 0.62
    left = (back[0] - half * math.sin(ang), back[1] + half * math.cos(ang))
    right = (back[0] + half * math.sin(ang), back[1] - half * math.cos(ang))
    d.polygon([tip, left, right], fill=fill)
    # round the two back corners a little
    for x, y in (left, right):
        d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=fill)


def arrow(d, points, width, head, fill=RED):
    """A path ending in an arrow head: the line stops short so the head's point stays sharp."""
    tip, before = points[-1], points[-2]
    ang = math.atan2(tip[1] - before[1], tip[0] - before[0])
    cut = (tip[0] - head * 0.7 * math.cos(ang), tip[1] - head * 0.7 * math.sin(ang))
    body = [p for p in points if math.hypot(p[0] - tip[0], p[1] - tip[1]) > head * 0.7] + [cut]
    stroke(d, body, width, fill)
    arrow_head(d, tip, cut, head, fill)


def car(img, cx, cy, length, width, heading, small):
    """A car in plan, centred on (cx, cy), facing heading degrees (0 up, 90 to the right)."""
    pad = 24
    tile = Image.new("RGBA", (width + 2 * pad, length + 2 * pad), (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)
    x0, y0, x1, y1 = pad, pad, pad + width, pad + length
    edge = 13 if small else 9
    # tyres, standing a little proud of the body
    tyre_l, tyre_w = length * 0.2, (16 if small else 12)
    for ty in (y0 + length * 0.16, y1 - length * 0.16 - tyre_l):
        for tx in (x0 - tyre_w * 0.55, x1 - tyre_w * 0.45):
            d.rounded_rectangle((tx, ty, tx + tyre_w, ty + tyre_l), radius=5, fill=CYAN_EDGE)
    d.rounded_rectangle((x0, y0, x1, y1), radius=width * 0.3, fill=CYAN, outline=CYAN_EDGE, width=edge)
    if not small:
        # windscreen (towards the front, which is up) and rear window
        d.rounded_rectangle((x0 + width * 0.2, y0 + length * 0.24, x1 - width * 0.2, y0 + length * 0.4), radius=7, fill=CYAN_EDGE)
        d.rounded_rectangle((x0 + width * 0.24, y1 - length * 0.27, x1 - width * 0.24, y1 - length * 0.17), radius=6, fill=CYAN_EDGE)
    else:
        d.rounded_rectangle((x0 + width * 0.26, y0 + length * 0.24, x1 - width * 0.26, y0 + length * 0.42), radius=6, fill=CYAN_EDGE)
    tile = tile.rotate(-heading, resample=Image.BICUBIC, expand=True)
    img.alpha_composite(tile, (int(round(cx - tile.width / 2)), int(round(cy - tile.height / 2))))


def click(d, x, y, small):
    """A point clicked on the path: a white dot ringed in the path's colour."""
    r = 20 if small else 15
    d.ellipse((x - r, y - r, x + r, y + r), fill=WHITE, outline=RED_EDGE, width=9 if small else 7)


# ---------------------------------------------------------------------- icons

def drive(small=False):
    """A car driven click by click: its path through a clicked point, ending in an arrow."""
    img, d = canvas()
    path = bezier((84, 150), (84, 82), (112, 58), (212, 58))
    arrow(d, path, 30 if small else 20, 66 if small else 50)
    mid = path[int(len(path) * 0.42)]
    click(d, mid[0], mid[1], small)
    car(img, 84, 186, 124, 70, 0, small)
    return img


def quick_drive(small=False):
    """The drive icon with a lightning bolt: drive without the dialog."""
    img = drive(small)
    d = ImageDraw.Draw(img)
    bolt = [(212, 118), (150, 196), (190, 196), (168, 250), (244, 164), (202, 164), (228, 118)]
    d.polygon(bolt, fill=GOLD)
    d.line(bolt + [bolt[0], bolt[1]], fill=GOLD_EDGE, width=11 if small else 8, joint="curve")
    return img


def turn_path(small=False):
    """A turning template: a car at the bottom left, its swept area curving away to the right."""
    img, d = canvas()
    cx = cy = 240
    inner, outer = 122, 210
    # the swept area: a band between two quarter arcs, soft inside and firm at its edges
    band = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(band)
    bd.polygon(arc(cx, cy, outer, 180, 270) + arc(cx, cy, inner, 270, 180), fill=RED_SOFT)
    img.alpha_composite(band)
    line = 22 if small else 13
    stroke(d, arc(cx, cy, outer, 180, 268), line, RED)
    stroke(d, arc(cx, cy, inner, 180, 266), line, RED)
    car(img, cx - (inner + outer) / 2, 182, 118, 68, 0, small)
    return img


def turn_edit(small=False):
    """The turning template with a pencil over it."""
    img = turn_path(small)
    d = ImageDraw.Draw(img)
    body = [(152, 196), (216, 132), (242, 158), (178, 222)]
    d.polygon(body, fill=ORANGE)
    d.line(body + [body[0], body[1]], fill=ORANGE_EDGE, width=11 if small else 8, joint="curve")
    tip = [(152, 196), (178, 222), (134, 240)]
    d.polygon(tip, fill=(250, 226, 190, 255))
    d.line(tip + [tip[0], tip[1]], fill=ORANGE_EDGE, width=11 if small else 8, joint="curve")
    d.polygon([(134, 240), (146, 222), (152, 234)], fill=ORANGE_EDGE)
    return img


def turn_solve(small=False):
    """A car turning round between two kerbs in three movements: forward, back, forward."""
    img, d = canvas()
    kerb = 24 if small else 16
    stroke(d, [(14, 22), (242, 22)], kerb, GREY)
    stroke(d, [(14, 234), (242, 234)], kerb, GREY)
    path = [(92, 166), (128, 76), (164, 184), (206, 84)]
    arrow(d, path + [(216, 60)], 26 if small else 18, 60 if small else 46)
    if not small:
        for x, y in path[1:3]:
            click(d, x, y, small)
    car(img, 60, 176, 104, 60, 20, small)
    return img


def turn_in_out(small=False):
    """A site with a driveway in its boundary: the path comes in forwards, stops, and goes out forwards."""
    img, d = canvas()
    wall = 24 if small else 16
    stroke(d, [(14, 84), (54, 84)], wall, GREY)
    stroke(d, [(202, 84), (242, 84)], wall, GREY)
    # in down the left, round the site, out up the right
    path = [(90, 20), (90, 130)] + arc(128, 130, 38, 180, 0)[1:] + [(166, 84)]
    arrow(d, path + [(166, 26)], 26 if small else 18, 60 if small else 46)
    car(img, 128, 204, 100, 58, 90, small)
    return img


def clearance(small=False):
    """A car in side view clearing a hump in the ground."""
    img, d = canvas()
    edge = 12 if small else 9
    # the ground, with a hump
    ground = [(8, 240), (8, 204), (70, 204)] + bezier((70, 204), (104, 204), (104, 160), (128, 160), 16) \
        + bezier((128, 160), (152, 160), (152, 204), (186, 204), 16) + [(248, 204), (248, 240)]
    d.polygon(ground, fill=EARTH)
    d.line(ground[1:-1], fill=EARTH_EDGE, width=edge, joint="curve")
    # the car: cabin, then body, on its wheels either side of the hump
    d.rounded_rectangle((84, 62, 184, 120), radius=24, fill=CYAN, outline=CYAN_EDGE, width=edge)
    d.rounded_rectangle((24, 104, 232, 160), radius=22, fill=CYAN, outline=CYAN_EDGE, width=edge)
    d.rectangle((92, 106, 176, 118), fill=CYAN)
    if not small:
        d.rounded_rectangle((100, 76, 168, 104), radius=10, fill=CYAN_EDGE)
        d.rectangle((131, 76, 137, 104), fill=CYAN)
    for cx in (62, 194):
        d.ellipse((cx - 23, 158, cx + 23, 204), fill=(40, 52, 64, 255))
        d.ellipse((cx - 8, 173, cx + 8, 189), fill=GREY)
    # the gap under the body at the crest
    d.polygon([(110, 192), (146, 192), (128, 164)], fill=RED)
    return img


def licence(small=False):
    """A key: signing in with an account."""
    img, d = canvas()
    edge = 13 if small else 10
    # the bow: a ring at the top left
    d.ellipse((26, 26, 142, 142), fill=GOLD, outline=GOLD_EDGE, width=edge)
    d.ellipse((60, 60, 108, 108), fill=(0, 0, 0, 0), outline=GOLD_EDGE, width=edge)
    # the shaft, running down to the right, with two teeth
    shaft = [(112, 130), (130, 112), (236, 218), (218, 236)]
    d.polygon(shaft, fill=GOLD)
    d.line(shaft + [shaft[0], shaft[1]], fill=GOLD_EDGE, width=edge, joint="curve")
    for (x, y) in ((168, 186), (198, 216)):
        tooth = [(x, y), (x - 22, y + 22), (x - 6, y + 38), (x + 16, y + 16)]
        d.polygon(tooth, fill=GOLD)
        d.line(tooth + [tooth[0], tooth[1]], fill=GOLD_EDGE, width=edge, joint="curve")
    return img


ICONS = {
    "drive": drive,
    "quickdrive": quick_drive,
    "turnpath": turn_path,
    "turnsolve": turn_solve,
    "turninout": turn_in_out,
    "turnedit": turn_edit,
    "clearance": clearance,
    "licence": licence,
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, draw in ICONS.items():
        draw(small=False).resize((32, 32), Image.LANCZOS).save(OUT / f"{name}_32.png")
        draw(small=True).resize((16, 16), Image.LANCZOS).save(OUT / f"{name}_16.png")
    print(f"wrote {len(ICONS) * 2} icons to {OUT}")


if __name__ == "__main__":
    main()
