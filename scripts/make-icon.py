"""生成 src/Pop/Assets/Pop.ico：蓝紫渐变的圆，中间一圈分成六格的白色圆环。

改图标时运行：python3 scripts/make-icon.py（需要 Pillow）
"""
import math
from pathlib import Path
from PIL import Image, ImageDraw

SIZE = 1024
img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

# 渐变底色
grad = Image.new("RGBA", (SIZE, SIZE))
top, bottom = (79, 140, 255), (142, 84, 233)
for y in range(SIZE):
    t = y / (SIZE - 1)
    c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
    ImageDraw.Draw(grad).line([(0, y), (SIZE, y)], fill=c)
mask = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(mask).ellipse([32, 32, SIZE - 32, SIZE - 32], fill=255)
img.paste(grad, (0, 0), mask)

# 六格圆环，正上方那一格高亮
d = ImageDraw.Draw(img)
c = SIZE / 2
outer, inner = 330, 190
gap = 7
for i in range(6):
    start = -90 - 30 + i * 60 + gap
    end = -90 + 30 + i * 60 - gap
    pts = []
    for a in range(int(start * 4), int(end * 4) + 1):
        r = math.radians(a / 4)
        pts.append((c + outer * math.cos(r), c + outer * math.sin(r)))
    for a in range(int(end * 4), int(start * 4) - 1, -1):
        r = math.radians(a / 4)
        pts.append((c + inner * math.cos(r), c + inner * math.sin(r)))
    d.polygon(pts, fill=(255, 255, 255, 255 if i == 0 else 150))
d.ellipse([c - 70, c - 70, c + 70, c + 70], fill=(255, 255, 255, 255))

out = Path(__file__).resolve().parent.parent / "src" / "Pop" / "Assets" / "Pop.ico"
out.parent.mkdir(parents=True, exist_ok=True)
img.save(out, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print(out)
