"""生成 src/Pop/Assets/Fonts 里的中文字体子集（Noto Sans CJK SC，常规和中等两个字重）。

需要 fonttools（pip install fonttools）和 Noto Sans CJK 的 TTC 文件（Debian / Ubuntu 的 fonts-noto-cjk、
fonts-noto-cjk-extra 包里有，也可以从 github.com/notofonts/noto-cjk 下载）：

    python3 scripts/make-fonts.py /usr/share/fonts/opentype/noto

子集包括 GB 2312 的全部字符、常用标点和符号、全角字符、西文，外加少量人名地名常用字。
"""
import os
import sys
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTCollection

EXTRA = "啰喔堃喆玥镕瑄睿琇嵘甯昇祎禕娴婕翀珉钰鑫淼焱垚犇骉昊旻暐晧皓锳嫚婳媛缇祺琪瑾瑜璟璐瑶瑛珂珏玹炜烨煜熠芃芊芮苒荭莜菡萱蓁蔚蕙薇"
RANGES = [(0x3000, 0x303F), (0xFF00, 0xFFEF), (0x2000, 0x206F), (0x00A0, 0x00FF), (0x2190, 0x21FF),
          (0x2460, 0x24FF), (0x25A0, 0x25FF), (0x2600, 0x26FF), (0x2E80, 0x2EFF), (0x3100, 0x312F), (0x31C0, 0x31EF)]


def characters():
    chars = set()
    for hi in range(0xA1, 0xF8):
        for lo in range(0xA1, 0xFF):
            try:
                chars.add(bytes([hi, lo]).decode("gb2312"))
            except UnicodeDecodeError:
                pass
    chars.update(chr(c) for c in range(0x20, 0x7F))
    for start, end in RANGES:
        chars.update(chr(c) for c in range(start, end))
    chars.update(EXTRA)
    return "".join(sorted(chars))


def main():
    source = Path(sys.argv[1] if len(sys.argv) > 1 else "/usr/share/fonts/opentype/noto")
    out = Path(__file__).resolve().parent.parent / "src" / "Pop" / "Assets" / "Fonts"
    out.mkdir(parents=True, exist_ok=True)
    text = characters()
    for weight in ["Regular", "Medium"]:
        collection = TTCollection(str(source / f"NotoSansCJK-{weight}.ttc"))
        font = next(f for f in collection.fonts if (f["name"].getDebugName(1) or "").startswith("Noto Sans CJK SC"))
        full = out / f"NotoSansSC-{weight}-full.otf"
        font.save(str(full))
        options = subset.Options()
        options.layout_features = ["*"]
        options.name_IDs = ["*"]
        options.name_languages = ["*"]
        options.notdef_outline = True
        options.hinting = False
        loaded = subset.load_font(str(full), options)
        subsetter = subset.Subsetter(options)
        subsetter.populate(text=text)
        subsetter.subset(loaded)
        subset.save_font(loaded, str(out / f"NotoSansSC-{weight}.otf"), options)
        os.remove(full)
        print(out / f"NotoSansSC-{weight}.otf")


if __name__ == "__main__":
    main()
