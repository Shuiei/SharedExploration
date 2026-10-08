# Draws thunderstore/icon.png (256x256): a parchment map, half explored, with footprints of three
# players meeting on it. python3 thunderstore/make_icon.py   (needs Pillow)
import os, random
from PIL import Image, ImageDraw

S = 4  # draw at 4x, then shrink for smooth edges
W = 256 * S
img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((8 * S, 8 * S, 248 * S, 248 * S), radius=40 * S, fill=(24, 32, 48, 255), outline=(70, 90, 120, 255), width=4 * S)
# Parchment.
d.rounded_rectangle((36 * S, 40 * S, 220 * S, 216 * S), radius=14 * S, fill=(214, 194, 150, 255), outline=(120, 92, 56, 255), width=4 * S)
# Fog over the unexplored part (right side, as clouds of grey).
random.seed(7)
fog = Image.new("RGBA", (W, W), (0, 0, 0, 0))
f = ImageDraw.Draw(fog)
for _ in range(60):
    x, y, r = random.randint(130, 216), random.randint(44, 212), random.randint(10, 22)
    f.ellipse(((x - r) * S, (y - r) * S, (x + r) * S, (y + r) * S), fill=(150, 150, 156, 255))
# Only on the parchment.
mask = Image.new("L", (W, W), 0)
ImageDraw.Draw(mask).rounded_rectangle((36 * S, 40 * S, 220 * S, 216 * S), radius=14 * S, fill=255)
img.paste(fog, (0, 0), Image.composite(fog, Image.new("RGBA", (W, W)), mask))
d = ImageDraw.Draw(img)
d.rounded_rectangle((36 * S, 40 * S, 220 * S, 216 * S), radius=14 * S, outline=(120, 92, 56, 255), width=4 * S)
# Land and sea on the explored part.
d.polygon([(48 * S, 70 * S), (96 * S, 58 * S), (124 * S, 88 * S), (110 * S, 140 * S), (70 * S, 150 * S), (52 * S, 120 * S)], fill=(126, 156, 84, 255))
d.polygon([(60 * S, 168 * S), (112 * S, 160 * S), (126 * S, 200 * S), (66 * S, 204 * S)], fill=(92, 140, 170, 255))
# Three dotted trails, in three colours, meeting at the middle.
cx, cy = 128, 128
for colour, start in (((230, 80, 60, 255), (56, 196)), ((70, 130, 230, 255), (60, 60)), ((240, 180, 40, 255), (206, 70))):
    for k in range(1, 9):
        t = k / 9
        x, y = start[0] + (cx - start[0]) * t, start[1] + (cy - start[1]) * t
        d.ellipse(((x - 4) * S, (y - 4) * S, (x + 4) * S, (y + 4) * S), fill=colour)
d.ellipse(((cx - 12) * S, (cy - 12) * S, (cx + 12) * S, (cy + 12) * S), fill=(250, 245, 230, 255), outline=(60, 44, 24, 255), width=4 * S)
img.resize((256, 256), Image.LANCZOS).save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.png"))
