# -*- coding: utf-8 -*-
"""给角色包的某一类动画换成自定义帧（GIF / 单张图 / 帧文件夹），或恢复默认。

用法：
    python anim_custom.py apply --pack "chars/小维" --slot click --src D:\\anim.gif
    python anim_custom.py apply --pack "chars/小维" --slot idle  --src D:\\frames
    python anim_custom.py reset --pack "chars/小维" --slot click

槽位：idle / press / click / drag
帧序固定为 待机 → 按键 → 点击 → 拖动；替换某一槽后会重新拼接 frames.bin 并更新计数。
"""
# 运行时不经过这个脚本：设置面板的「新建角色 / 改大小 / 换动画」走 SpriteGen.cs。
# 本脚本只作批量 / 开发工具保留；改动画数学时两边要同步，输出需能互相替换
# （frames.bin 格式完全一致）。

import argparse
import json
import os
import shutil
import sys
import tempfile

import numpy as np
from PIL import Image, ImageSequence

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SLOTS = ("idle", "press", "click", "drag")
MIN_FRAMES = {"idle": 2, "press": 4, "click": 4, "drag": 1}
IMG_EXT = (".png", ".jpg", ".jpeg", ".webp", ".bmp")


def read_manifest(d):
    m = {}
    p = os.path.join(d, "char.json")
    if os.path.exists(p):
        for line in open(p, encoding="utf-8-sig"):
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                m[k.strip()] = v.strip()
    return m


def write_manifest(d, m):
    order = ["name", "image", "frames", "meta", "lines", "voicelines", "press", "release",
             "charH", "ampIdle", "ampPress", "ampClick", "ampDrag",
             "customIdle", "customPress", "customClick", "customDrag", "voices", "created"]
    keys = [k for k in order if k in m] + [k for k in m if k not in order]
    with open(os.path.join(d, "char.json"), "w", encoding="utf-8", newline="") as fh:
        for k in keys:
            fh.write("%s=%s\r\n" % (k, m[k]))


def read_meta(d):
    meta = {}
    for line in open(os.path.join(d, "动画", "frames.txt"), encoding="utf-8-sig"):
        if "=" in line:
            k, v = line.strip().split("=", 1)
            meta[k] = int(v)
    return meta


def premultiplied(im):
    a = np.asarray(im.convert("RGBA")).astype(np.uint16)
    al = a[:, :, 3:4]
    rgb = (a[:, :, :3] * al + 127) // 255
    out = np.dstack([rgb[:, :, 2], rgb[:, :, 1], rgb[:, :, 0], a[:, :, 3]]).astype(np.uint8)
    return out.tobytes()


def fit_frame(im, w, h):
    im = im.convert("RGBA")
    k = min(w / float(im.width), h / float(im.height))
    nw, nh = max(1, int(round(im.width * k))), max(1, int(round(im.height * k)))
    im = im.resize((nw, nh), Image.LANCZOS)
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    canvas.alpha_composite(im, ((w - nw) // 2, h - nh))   # 底部居中，和默认动画脚底锚定一致
    return canvas


def load_source(src):
    """返回 PIL 帧列表。"""
    if os.path.isdir(src):
        files = sorted(f for f in os.listdir(src) if os.path.splitext(f)[1].lower() in IMG_EXT)
        if not files:
            raise SystemExit("文件夹里没有图片（png/jpg/webp/bmp）")
        return [Image.open(os.path.join(src, f)).convert("RGBA") for f in files]
    im = Image.open(src)
    if getattr(im, "n_frames", 1) > 1:
        return [fr.convert("RGBA") for fr in ImageSequence.Iterator(im)]
    return [im.convert("RGBA")]


def load_segments(d, meta):
    w, h = meta["w"], meta["h"]
    fb = w * h * 4
    blob = open(os.path.join(d, "动画", "frames.bin"), "rb").read()
    counts = {s: meta.get(s, 0) for s in SLOTS}
    segs, pos = {}, 0
    for s in SLOTS:
        n = counts[s]
        if pos + n * fb > len(blob):
            raise SystemExit("frames.bin 和 frames.txt 不一致，请先 rebuild 角色")
        segs[s] = [blob[pos + i * fb: pos + (i + 1) * fb] for i in range(n)]
        pos += n * fb
    return segs, counts, w, h, fb


def save_pack(d, segs, counts, w, h):
    anim = os.path.join(d, "动画")
    blob = b"".join(b"".join(segs[s]) for s in SLOTS)
    with open(os.path.join(anim, "frames.bin"), "wb") as fh:
        fh.write(blob)
    meta = {"w": w, "h": h, "idle": counts["idle"], "press": counts["press"],
            "click": counts["click"], "drag": counts["drag"], "fps": 30,
            "frameBytes": w * h * 4}
    with open(os.path.join(anim, "frames.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    with open(os.path.join(anim, "frames.txt"), "w", encoding="utf-8", newline="") as fh:
        fh.write("w=%d\r\nh=%d\r\nidle=%d\r\npress=%d\r\nclick=%d\r\ndrag=%d\r\nfps=%d\r\n"
                 % (w, h, counts["idle"], counts["press"], counts["click"], counts["drag"], 30))


def apply_custom(d, slot, src):
    meta = read_meta(d)
    segs, counts, w, h, fb = load_segments(d, meta)
    images = load_source(src)
    if len(images) < MIN_FRAMES[slot]:
        # 帧数不够就重复最后一帧，至少保证状态机能正常回弹
        while len(images) < MIN_FRAMES[slot]:
            images.append(images[-1].copy())
    frames = [premultiplied(fit_frame(im, w, h)) for im in images]
    segs[slot] = frames
    counts[slot] = len(frames)
    save_pack(d, segs, counts, w, h)

    m = read_manifest(d)
    m["custom" + slot[0].upper() + slot[1:]] = "1"
    write_manifest(d, m)
    print("已替换 %s 动画：%d 帧（%dx%d）" % (slot, len(frames), w, h))


def reset_slot(d, slot):
    import sprites

    m = read_manifest(d)
    meta = read_meta(d)
    segs, counts, w, h, fb = load_segments(d, meta)

    image = os.path.join(d, "图片", "image.png")
    if not os.path.exists(image):
        for ext in IMG_EXT:
            p = os.path.join(d, "图片", "image" + ext)
            if os.path.exists(p):
                image = p
                break
    amps = {}
    for k in ("idle", "press", "click", "drag"):
        try:
            amps[k] = float(m.get("amp" + k[0].upper() + k[1:], "1") or 1)
        except ValueError:
            amps[k] = 1.0
    tmp = tempfile.mkdtemp(prefix="fanim_")
    try:
        sprites.build(image, int(m.get("charH", 219)), tmp, amps, make_preview=False)
        dmeta = {}
        for line in open(os.path.join(tmp, "frames.txt"), encoding="utf-8"):
            if "=" in line:
                k, v = line.strip().split("=", 1)
                dmeta[k] = int(v)
        blob = open(os.path.join(tmp, "frames.bin"), "rb").read()
        dfb = dmeta["w"] * dmeta["h"] * 4
        start = sum(dmeta[s] for s in SLOTS[:SLOTS.index(slot)]) * dfb
        n = dmeta[slot]
        segs[slot] = [blob[start + i * dfb: start + (i + 1) * dfb] for i in range(n)]
        counts[slot] = n
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    save_pack(d, segs, counts, w, h)
    m.pop("custom" + slot[0].upper() + slot[1:], None)
    write_manifest(d, m)
    print("已恢复 %s 默认动画：%d 帧" % (slot, counts[slot]))


def main():
    ap = argparse.ArgumentParser(description="自定义/恢复某一类动画")
    sub = ap.add_subparsers(dest="cmd")
    for name in ("apply", "reset"):
        p = sub.add_parser(name)
        p.add_argument("--pack", required=True)
        p.add_argument("--slot", required=True, choices=SLOTS)
        if name == "apply":
            p.add_argument("--src", required=True)
    a = ap.parse_args()
    d = a.pack if os.path.isdir(a.pack) else os.path.join(ROOT, "chars", a.pack)
    if not os.path.isdir(d):
        raise SystemExit("找不到角色包：%s" % d)
    if a.cmd == "apply":
        apply_custom(d, a.slot, a.src)
    elif a.cmd == "reset":
        reset_slot(d, a.slot)
    else:
        ap.print_help()


if __name__ == "__main__":
    main()
