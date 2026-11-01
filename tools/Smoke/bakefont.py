# Bakes a font atlas with Pillow for the headless harness (the game itself bakes with GDI on Windows).
# usage: bakefont.py <ttf> <px> <xscale> <out.png> <out.json>
import sys, json
from PIL import Image, ImageDraw, ImageFont
ttf, px, xs, outp, outj = sys.argv[1], int(sys.argv[2]), float(sys.argv[3]), sys.argv[4], sys.argv[5]
chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·—–…←↑→↓°×±éèàüöä▲▼◆●○■□★♪∞♥✓✕•’‘“”⚠"
f = ImageFont.truetype(ttf, px)
asc, desc = f.getmetrics(); lh = asc + desc
cols = 16; rows = (len(chars) + cols - 1) // cols
adv = {c: f.getlength(c) * xs for c in chars}
cw = int(max(adv.values()) + 6); cell = lh + 4
W = 64
while W < cw * cols: W *= 2
H = 64
while H < cell * rows: H *= 2
img = Image.new("L", (W, H), 0); glyphs = {}
for i, c in enumerate(chars):
    x = (i % cols) * cw + 2; y = (i // cols) * cell + 2
    g = Image.new("L", (int(f.getlength(c)) + 8, lh + 4), 0)
    ImageDraw.Draw(g).text((2, 0), c, font=f, fill=255)
    if xs != 1.0: g = g.resize((max(1, int(g.width * xs)), g.height), Image.LANCZOS)
    img.paste(g, (x, y))
    glyphs[c] = [x, y, int(adv[c] + 2), lh, adv[c]]
img.save(outp)
json.dump({"size": px, "ascent": asc, "lineHeight": lh, "glyphs": glyphs}, open(outj, "w"))
