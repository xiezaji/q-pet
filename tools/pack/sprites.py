# -*- coding: utf-8 -*-
"""生成独立桌宠用的精灵帧（原始 BGRA，预乘 alpha，供 C# 直接 blit，无需任何图像库）。

帧序：[0, IDLE_N) 待机呼吸 → [IDLE_N, IDLE_N+PRESS_N) 按压弹跳 →
      [IDLE_N+PRESS_N, …) 拖动悬空摆动。
锚点：脚底固定在画布底部居中（拖动帧会整体提起，脚不再锚定）。

两个踩过的坑：
  1. UpdateLayeredWindow 要求 32bpp **预乘 alpha**，所以这里直接输出预乘后的 BGRA，
     否则半透明边缘会发白/发亮。
  2. `scaled()` 里**绝不能**把放大后的图合成回原宽画布 —— 负偏移会把两侧裁掉，
     而且外层画布加多宽都没用（裁切发生在更早一步）。所以缩放和贴图彻底分开。
"""
# 运行时不经过这个脚本：设置面板的「新建角色 / 改大小 / 换动画」走 SpriteGen.cs。
# 本脚本只作批量 / 开发工具保留；改动画数学时两边要同步，输出需能互相替换
# （frames.bin 格式完全一致）。

import json
import os
import re
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OVERRIDES = os.path.join(ROOT, "pet-overrides.txt")


def _override(key, default):
    """读取设置面板写下的覆盖值。

    用正则按 "key=" 切分：既支持一行一个，也容忍整份被写成一整行；
    值里带空格（含空格的路径）也不会切错。
    """
    try:
        txt = open(OVERRIDES, encoding="utf-8-sig").read()
        for m in re.finditer(r"([A-Za-z0-9_]+)=([^\r\n]*?)(?=\s+[A-Za-z0-9_]+=|\s*$)", txt):
            if m.group(1) == key:
                return m.group(2).strip()
    except Exception:
        pass
    return default


def _arg(name, default):
    if name in sys.argv:
        i = sys.argv.index(name)
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    return default


# 角色图与大小：命令行 --src/--char-h 优先，其次 pet-overrides.txt
SRC = _arg("--src", _override("charSrc", os.path.join(HERE, "furina-v4.png")))
CHAR_H = int(_arg("--char-h", _override("charH", "300")))
BOTTOM_PAD = 6                                        # 脚底留边，防最后一行被窗口边界切掉
IDLE_N = 24
PRESS_N = 6
CLICK_N = 6                                            # 点击：比按键更深一点 + 小跳
DRAG_N = 8                                            # 拖动：悬空摆动
FPS = 30

# 按压序列：(scaleX, scaleY)，脚底锚定
PRESS = [(1.00, 1.00), (1.06, 0.93), (1.10, 0.86), (1.08, 0.85), (1.03, 0.95), (1.00, 1.00)]
# 点击序列：压得更深，同时向上小跳（dy 为负 = 提起）
CLICK = [(1.00, 1.00), (1.09, 0.88), (1.15, 0.81), (1.11, 0.84), (1.03, 0.96), (1.00, 1.00)]
CLICK_DY = [0, -2, -5, -4, -1, 0]
# 拖动序列：轻微旋转 + 左右摆 + 整体提起（悬空感）
DRAG_ANGLE = [-5, -3, 0, 3, 5, 3, 0, -3]
DRAG_DX = [6, 3, 0, -3, -6, -3, 0, 3]
DRAG_LIFT = 14
DRAG_SHRINK = 0.95


def fit(im, w, h):
    """等比缩放到高度 h，宽度据比例。"""
    k = h / float(im.height)
    return im.resize((max(1, int(round(im.width * k))), h), Image.LANCZOS)


def place(im, canvas_w, canvas_h, dy=0, dx=0):
    """居中 + 底部锚定（BOTTOM_PAD 防裁切）；dx/dy 额外偏移。居中归这里管，缩放那边不裁切。"""
    c = Image.new("RGBA", (canvas_w, canvas_h), (0, 0, 0, 0))
    c.alpha_composite(im, ((canvas_w - im.width) // 2 + int(round(dx)),
                           canvas_h - im.height + int(round(dy)) - BOTTOM_PAD))
    return c


def scaled(im, sx, sy):
    """挤压/拉伸：**只缩放，绝不裁切**（居中和锚定交给 place()）。

    这里原来有个 bug：把放大后的图 alpha_composite 回**原宽**画布，居中偏移算出来是负数
    （例如 290 宽放进 264 画布 → x = -13），两侧当场被切掉 —— 所以按压时角色缺一块，
    而且外层画布加多宽都没用（裁切发生在更早一步）。
    """
    w = max(1, int(round(im.width * sx)))
    h = max(1, int(round(im.height * sy)))
    return im.resize((w, h), Image.LANCZOS)


def premultiplied_bgra(im):
    a = np.asarray(im.convert("RGBA")).astype(np.uint16)
    al = a[:, :, 3:4]
    rgb = (a[:, :, :3] * al + 127) // 255          # 预乘
    out = np.dstack([rgb[:, :, 2], rgb[:, :, 1], rgb[:, :, 0], a[:, :, 3]]).astype(np.uint8)
    return out.tobytes()


def _squash(pairs, amp):
    """把挤压序列按幅度系数缩放（1.0 = 原始幅度，0 = 几乎不动）。"""
    a = float(amp)
    return [(1.0 + (sx - 1.0) * a, 1.0 + (sy - 1.0) * a) for sx, sy in pairs]


def build(src_path, char_h, out_dir, amps=None, make_preview=True):
    """给一个角色生成整套精灵帧，写到 out_dir（角色包的「动画」目录），返回 meta。

    帧序：待机 → 按键 → 点击 → 拖动。四段各自有幅度参数（amps: idle/press/click/drag），
    倍速由桌宠运行时控制，不需要在这里烘进帧里。
    """
    amps = amps or {}
    a_idle = max(0.0, float(amps.get("idle", 1.0)))
    a_press = max(0.0, float(amps.get("press", 1.0)))
    a_click = max(0.0, float(amps.get("click", 1.0)))
    a_drag = max(0.0, float(amps.get("drag", 1.0)))

    char_h = int(char_h)
    canvas_w = int(round(char_h * 320.0 / 300.0))
    canvas_h = int(round(char_h * 324.0 / 300.0))
    src = Image.open(src_path).convert("RGBA")
    char = fit(src, int(round(src.width * char_h / src.height)), char_h)
    print("角色缩放后 %dx%d，画布 %dx%d" % (char.width, char.height, canvas_w, canvas_h))

    frames = []
    for i in range(IDLE_N):                                  # 待机：浮动 + 呼吸
        t = 2 * np.pi * i / IDLE_N
        frames.append(place(scaled(char, 1.0, 1.0 + 0.010 * a_idle * (0.5 + 0.5 * np.sin(t))),
                            canvas_w, canvas_h, dy=2.5 * a_idle * np.sin(t)))
    for sx, sy in _squash(PRESS, a_press):                   # 按键：挤压-拉伸
        frames.append(place(scaled(char, sx, sy), canvas_w, canvas_h))
    for i, (sx, sy) in enumerate(_squash(CLICK, a_click)):   # 点击：更深 + 小跳
        frames.append(place(scaled(char, sx, sy), canvas_w, canvas_h, dy=CLICK_DY[i] * a_click))
    small = char.resize((max(1, int(char.width * DRAG_SHRINK)),
                         max(1, int(char.height * DRAG_SHRINK))), Image.LANCZOS)
    for i in range(DRAG_N):                                  # 拖动：悬空摆动
        rot = small.rotate(DRAG_ANGLE[i] * a_drag, resample=Image.BICUBIC, expand=False, fillcolor=(0, 0, 0, 0))
        frames.append(place(rot, canvas_w, canvas_h, dy=-DRAG_LIFT * a_drag, dx=DRAG_DX[i] * a_drag))

    os.makedirs(out_dir, exist_ok=True)
    blob = b"".join(premultiplied_bgra(f) for f in frames)
    p_bin = os.path.join(out_dir, "frames.bin")
    with open(p_bin, "wb") as fh:
        fh.write(blob)
    meta = {"w": canvas_w, "h": canvas_h, "idle": IDLE_N, "press": PRESS_N, "click": CLICK_N,
            "drag": DRAG_N, "fps": FPS, "frameBytes": canvas_w * canvas_h * 4}
    with open(os.path.join(out_dir, "frames.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)
    # 再写一份 key=value 版：C# 侧不引入 JSON 库也能读
    with open(os.path.join(out_dir, "frames.txt"), "w", encoding="utf-8") as fh:
        fh.write("w=%d\nh=%d\nidle=%d\npress=%d\nclick=%d\ndrag=%d\nfps=%d\n"
                 % (canvas_w, canvas_h, IDLE_N, PRESS_N, CLICK_N, DRAG_N, FPS))
    print("精灵帧 %s  %d 帧（待机%d + 按键%d + 点击%d + 拖动%d）  %.1f MB"
          % (p_bin, len(frames), IDLE_N, PRESS_N, CLICK_N, DRAG_N, os.path.getsize(p_bin) / 1048576.0))

    if make_preview:
        # 预览：待机 / 按键 / 点击 / 拖动
        picks = [frames[0], frames[IDLE_N + 2], frames[IDLE_N + PRESS_N + 2],
                 frames[IDLE_N + PRESS_N + CLICK_N + 4]]
        gap = 8
        sheet = Image.new("RGBA", (canvas_w * len(picks) + gap * (len(picks) + 1), canvas_h + 2 * gap),
                          (18, 24, 48, 255))
        for i, f in enumerate(picks):
            sheet.alpha_composite(f, (gap + i * (canvas_w + gap), gap))
        sheet.convert("RGB").save(os.path.join(out_dir, "preview.png"))
        print("预览 preview.png（待机 / 按键 / 点击 / 拖动）")
    return meta


def main():
    # 兼容旧用法：--src/--char-h 不传就沿用 pet-overrides.txt；--out-dir 指定输出目录
    out_dir = _arg("--out-dir", ROOT)
    src = _arg("--src", SRC)
    char_h = int(_arg("--char-h", CHAR_H))
    amps = {
        "idle": float(_arg("--amp-idle", 1.0)),
        "press": float(_arg("--amp-press", 1.0)),
        "click": float(_arg("--amp-click", 1.0)),
        "drag": float(_arg("--amp-drag", 1.0)),
    }
    build(src, char_h, out_dir, amps, make_preview=("--no-preview" not in sys.argv))


if __name__ == "__main__":
    main()
