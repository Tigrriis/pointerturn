"""Generate the ribbon icons in src/PointerTurn.BricsCAD/Resources.

Run from the repo root:  python tools/icons/make_icons.py   (needs Pillow)

Each icon is drawn at 8x on a transparent canvas and downsampled, giving
smooth 16 px (small button) and 32 px (large button) PNGs. Colours are
mid-tones with dark outlines so they read on BricsCAD's light and dark themes.
"""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[2] / "src" / "PointerTurn.BricsCAD" / "Resources"
S = 256  # drawing canvas; 8x the large size

BLUE = (59, 130, 246, 255)
BLUE_DARK = (29, 78, 160, 255)
ORANGE = (224, 122, 79, 255)
ORANGE_DARK = (150, 70, 40, 255)
GREEN = (34, 160, 90, 255)
GREEN_DARK = (16, 100, 55, 255)
GOLD = (230, 175, 50, 255)
GOLD_DARK = (140, 100, 20, 255)
WHITE = (255, 255, 255, 255)
GREY = (90, 100, 115, 255)
OUTLINE = 10


def canvas():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def turn_path():
    """A car in plan at the bottom left, with its swept path curving away to the right."""
    img, d = canvas()
    RED = (205, 55, 45, 255)
    # swept path: a band between two concentric quarter arcs about (236, 236)
    d.pieslice((236 - 206, 236 - 206, 236 + 206, 236 + 206), 180, 270, fill=RED)
    d.pieslice((236 - 118, 236 - 118, 236 + 118, 236 + 118), 180, 270, fill=(0, 0, 0, 0))
    # dashed clearance line outside the band
    for a in range(182, 270, 16):
        d.arc((236 - 236, 236 - 236, 236 + 236, 236 + 236), a, a + 8, fill=RED, width=10)
    # the car, parked at the start, pointing up
    d.rounded_rectangle((52, 118, 128, 238), radius=18, fill=BLUE, outline=BLUE_DARK, width=OUTLINE)
    for (x0, y0) in ((44, 132), (118, 132), (44, 196), (118, 196)):
        d.rectangle((x0, y0, x0 + 18, y0 + 30), fill=BLUE_DARK)
    return img


def turn_edit():
    """The turn path icon with a pencil over it."""
    img = turn_path()
    d = ImageDraw.Draw(img)
    body = [(150, 196), (214, 132), (238, 156), (174, 220)]
    d.polygon(body, fill=ORANGE, outline=ORANGE_DARK)
    d.line(body + [body[0]], fill=ORANGE_DARK, width=OUTLINE, joint="curve")
    tip = [(150, 196), (174, 220), (136, 234)]
    d.polygon(tip, fill=(245, 215, 170, 255))
    d.line(tip + [tip[0]], fill=ORANGE_DARK, width=OUTLINE, joint="curve")
    return img


def turn_solve():
    """A car turning round between two kerbs in three movements: forward, back, forward."""
    img, d = canvas()
    RED = (205, 55, 45, 255)
    # the kerbs
    d.line([(8, 22), (248, 22)], fill=GREY, width=18)
    d.line([(8, 234), (248, 234)], fill=GREY, width=18)
    # the three movements, the last with an arrow head
    path = [(70, 176), (112, 70), (156, 190), (204, 86)]
    d.line(path, fill=RED, width=20, joint="curve")
    for (x, y) in path[:-1]:
        d.ellipse((x - 10, y - 10, x + 10, y + 10), fill=RED)
    d.polygon([(222, 44), (176, 84), (232, 104)], fill=RED)
    # the car at the start
    d.rounded_rectangle((14, 160, 84, 210), radius=16, fill=BLUE, outline=BLUE_DARK, width=OUTLINE)
    return img


def turn_in_out():
    """A site with a driveway in its boundary: the path comes in forwards, stops, and goes out forwards."""
    img, d = canvas()
    RED = (205, 55, 45, 255)
    # the site boundary, with the driveway gap
    d.line([(8, 78), (52, 78)], fill=GREY, width=18)
    d.line([(204, 78), (248, 78)], fill=GREY, width=18)
    # in down the left, round the site, out up the right
    path = [(88, 14), (88, 150), (106, 196), (128, 206), (150, 196), (168, 150), (168, 64)]
    d.line(path, fill=RED, width=20, joint="curve")
    d.polygon([(168, 10), (136, 66), (200, 66)], fill=RED)
    # the car stopped on the site
    d.rounded_rectangle((94, 186, 164, 236), radius=16, fill=BLUE, outline=BLUE_DARK, width=OUTLINE)
    return img


def clearance():
    """A car in side view clearing a hump in the ground."""
    img, d = canvas()
    GROUND = (120, 90, 60, 255)
    # ground with a hump
    d.polygon([(8, 236), (8, 200), (70, 200), (128, 150), (186, 200), (248, 200), (248, 236)], fill=GROUND)
    # chassis line and body
    d.rounded_rectangle((30, 90, 226, 152), radius=20, fill=BLUE, outline=BLUE_DARK, width=OUTLINE)
    d.rounded_rectangle((78, 50, 190, 100), radius=18, fill=BLUE, outline=BLUE_DARK, width=OUTLINE)
    d.rectangle((78, 90, 190, 100), fill=BLUE)
    # wheels on the ground either side of the hump
    for cx in (62, 194):
        d.ellipse((cx - 26, 148, cx + 26, 200), fill=GREY, outline=BLUE_DARK, width=OUTLINE)
    # the clearance under the body at the crest, in red
    d.line([(128, 152), (128, 150)], fill=(205, 55, 45, 255), width=6)
    d.polygon([(110, 170), (146, 170), (128, 152)], fill=(205, 55, 45, 255))
    return img


def licence():
    """A key: signing in with an account."""
    img, d = canvas()
    # the bow: a ring at the top left
    d.ellipse((28, 28, 140, 140), fill=GOLD, outline=GOLD_DARK, width=OUTLINE)
    d.ellipse((62, 62, 106, 106), fill=(0, 0, 0, 0), outline=GOLD_DARK, width=OUTLINE)
    # the shaft, running down to the right, with two teeth
    shaft = [(112, 128), (128, 112), (232, 216), (216, 232)]
    d.polygon(shaft, fill=GOLD)
    d.line(shaft + [shaft[0]], fill=GOLD_DARK, width=OUTLINE, joint="curve")
    for (x, y) in ((176, 192), (204, 220)):
        tooth = [(x, y), (x - 24, y + 24), (x - 8, y + 40), (x + 16, y + 16)]
        d.polygon(tooth, fill=GOLD)
        d.line(tooth + [tooth[0]], fill=GOLD_DARK, width=OUTLINE, joint="curve")
    return img


ICONS = {
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
        big = draw()
        for size in (16, 32):
            big.resize((size, size), Image.LANCZOS).save(OUT / f"{name}_{size}.png")
    print(f"wrote {len(ICONS) * 2} icons to {OUT}")


if __name__ == "__main__":
    main()
