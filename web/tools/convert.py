# -*- coding: utf-8 -*-
"""把桌面版角色包转成网页版资源：立绘 WebP、语音 MP3、清单 JSON。

用法：python tools/convert.py <furina-widget 目录>
"""
import json
import os
import shutil
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "assets")

CHARS = [("芙宁娜", "furina"), ("阿米娅", "amiya"), ("小维", "xiaowei"), ("奶蛙", "naiwa")]
MAX_H = 860


def kv(path):
    d = {}
    for line in open(path, encoding="utf-8-sig"):
        if "=" in line:
            k, v = line.rstrip("\r\n").split("=", 1)
            d[k.strip()] = v.strip()
    return d


def to_mp3(src, dst):
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", src, "-ac", "1", "-ar", "32000",
                    "-b:a", "80k", dst], check=True)


def convert_char(root, name, cid):
    src = os.path.join(root, "chars", name)
    out = os.path.join(OUT, "chars", cid)
    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(os.path.join(out, "voices"))
    meta = kv(os.path.join(src, "char.json"))

    im = Image.open(os.path.join(src, meta.get("image", "图片/image.png"))).convert("RGBA")
    if im.height > MAX_H:
        im = im.resize((round(im.width * MAX_H / im.height), MAX_H), Image.LANCZOS)
    im.save(os.path.join(out, "image.webp"), "WEBP", quality=90, method=6)

    voices, by_path = [], {}
    tsv = os.path.join(src, meta.get("voicelines", "音效/voices.tsv"))
    if os.path.exists(tsv):
        for line in open(tsv, encoding="utf-8-sig"):
            line = line.rstrip("\r\n")
            if "\t" not in line:
                continue
            text, rel = line.split("\t", 1)
            p = os.path.join(src, rel.strip())
            if not text or not os.path.exists(p):
                continue
            fn = "v%02d.mp3" % (len(voices) + 1)
            to_mp3(p, os.path.join(out, "voices", fn))
            by_path[os.path.normcase(os.path.abspath(p))] = fn
            voices.append({"text": text, "file": "voices/" + fn})

    def sfx(key):
        rel = meta.get(key, "")
        if not rel:
            return ""
        p = os.path.normcase(os.path.abspath(os.path.join(src, rel)))
        if p in by_path:
            return "voices/" + by_path[p]
        if not os.path.exists(p):
            return ""
        fn = key + ".mp3"
        to_mp3(p, os.path.join(out, fn))
        return fn

    lines = []
    lp = os.path.join(src, meta.get("lines", "台词.txt"))
    if os.path.exists(lp):
        lines = [l.strip() for l in open(lp, encoding="utf-8-sig") if l.strip()]

    amp = {k: float(meta.get("amp" + k.capitalize(), "1")) for k in ("idle", "press", "click", "drag")}
    data = {"id": cid, "name": meta.get("name", name), "image": "image.webp",
            "imageW": Image.open(os.path.join(src, meta.get("image", "图片/image.png"))).width,
            "imageH": Image.open(os.path.join(src, meta.get("image", "图片/image.png"))).height,
            "charH": int(meta.get("charH", "219")), "amp": amp,
            "press": sfx("press"), "release": sfx("release"),
            "voices": voices, "lines": lines}
    with open(os.path.join(out, "char.json"), "w", encoding="utf-8") as fh:
        json.dump(data, fh, ensure_ascii=False, indent=1)
    print("%s → %s  语音 %d  台词 %d" % (name, cid, len(voices), len(lines)))
    return {"id": cid, "name": data["name"]}


def convert_fonts(root):
    from fontTools.ttLib import TTFont
    dst = os.path.join(OUT, "fonts")
    os.makedirs(dst, exist_ok=True)
    for w in ("Medium", "SemiBold", "Bold"):
        f = TTFont(os.path.join(root, "Fonts", "SairaWide-%s.ttf" % w))
        f.flavor = "woff2"
        f.save(os.path.join(dst, "SairaWide-%s.woff2" % w))


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser("~/Desktop/furina-widget")
    os.makedirs(os.path.join(OUT, "chars"), exist_ok=True)
    index = [convert_char(root, n, c) for n, c in CHARS]
    with open(os.path.join(OUT, "chars", "index.json"), "w", encoding="utf-8") as fh:
        json.dump(index, fh, ensure_ascii=False, indent=1)
    convert_fonts(root)
    icon = Image.open(os.path.join(root, "docs", "images", "icon.png")).convert("RGBA")
    icon.resize((128, 128), Image.LANCZOS).save(os.path.join(OUT, "icon.png"))
    icon.resize((32, 32), Image.LANCZOS).save(os.path.join(OUT, "favicon.png"))


if __name__ == "__main__":
    main()
