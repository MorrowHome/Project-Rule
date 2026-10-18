
from PIL import Image, ImageDraw

S = 4
W = 320 * S
img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
def P(x, y): return (x * S, y * S)

nose  = P(298, 154)
upper = P(20, 82)
notch = P(126, 154)
lower = P(58, 236)

paper_light = (248, 244, 232, 255)
paper_mid   = (232, 224, 205, 255)
paper_dark  = (206, 196, 174, 255)
ink         = (33, 35, 44, 255)

d.polygon([nose, notch, lower], fill=paper_dark)          # keel (in shadow)
d.polygon([nose, upper, notch], fill=paper_light)         # wing (lit)
# slim secondary facet along the leading edge reads as the far wing
d.polygon([nose, upper, P(150, 120)], fill=paper_mid)

d.line([nose, upper], fill=(255, 253, 246, 255), width=int(6 * S))
d.line([nose, upper, notch, lower, nose], fill=ink, width=int(9 * S), joint="curve")
d.line([nose, notch], fill=ink, width=int(6 * S))

img = img.resize((320, 320), Image.LANCZOS)
img.save(r"D:\project\GameJam\Project-Rule\Assets\_Project\Art\Sprites\Player\PaperPlane_01.png")
print("saved")