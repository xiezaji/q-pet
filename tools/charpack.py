# -*- coding: utf-8 -*-
"""角色包管理（每个角色一个文件夹）

结构：
    chars/<角色名>/
        char.json          角色清单（相对路径，整个文件夹可以随便搬）
        图片/image.png     立绘（原图，越大越清楚）
        动画/frames.bin    动画帧（sprites.py 生成）
        动画/frames.txt
        动画/frames.json
        动画/preview.png
        音效/voices.tsv    台词 <TAB> 语音（相对路径）
        音效/voices/*.wav  台词语音
        音效/press.wav     可选：点击按压音（没有就只播台词）
        音效/release.wav   可选：松开音
        台词.txt           气泡随机台词（一行一句）

用法：
    python charpack.py list
    python charpack.py new --name 新角色 --image D:\\pics\\a.png --voices D:\\voices --char-h 219
    python charpack.py rebuild --name 新角色 --char-h 260

「新建角色」= 新建一个文件夹 + 把用户给的东西（图片 / 语音文件夹 / 台词）放进去，
再生成动画帧。设置面板的「导入」页走的也是同一套结构。
"""
import argparse
import os
import re
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CHARS = os.path.join(HERE, "chars")
IMG_DIR, ANIM_DIR, SFX_DIR = "图片", "动画", "音效"
VOICE_DIR = os.path.join(SFX_DIR, "voices")
AUDIO_EXT = {".wav", ".mp3", ".m4a", ".ogg", ".flac"}
DEFAULT_H = 219
BAD_NAME = set('\\/:*?"<>|')


def safe_name(name):
    s = "".join(("-" if c in BAD_NAME else c) for c in (name or "").strip())
    return s.strip(" .")


def pack_dir(name):
    return os.path.join(CHARS, safe_name(name))


def read_manifest(name_or_dir):
    d = name_or_dir if os.path.isdir(name_or_dir) else pack_dir(name_or_dir)
    p = os.path.join(d, "char.json")
    out = {}
    if os.path.exists(p):
        for line in open(p, encoding="utf-8-sig"):
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            out[k.strip()] = v.strip()
    return out


def write_manifest(d, data):
    order = ["name", "image", "frames", "meta", "lines", "voicelines",
             "press", "release", "charH", "ampIdle", "ampPress", "ampClick", "ampDrag",
             "voices", "created"]
    keys = [k for k in order if k in data] + [k for k in data if k not in order]
    with open(os.path.join(d, "char.json"), "w", encoding="utf-8", newline="") as fh:
        for k in keys:
            fh.write("%s=%s\r\n" % (k, data[k]))


def rel(p):
    """清单里统一用正斜杠（Windows 也能正常读），整个文件夹搬走不会坏。"""
    return p.replace("\\", "/")


def find_image(d):
    for ext in (".png", ".jpg", ".jpeg", ".webp", ".bmp"):
        p = os.path.join(d, IMG_DIR, "image" + ext)
        if os.path.exists(p):
            return p
    return ""


def ffmpeg():
    p = shutil.which("ffmpeg")
    if p:
        return p
    for c in (r"D:\f\ffmpeg-7.1-essentials_build\bin\ffmpeg.exe",):
        if os.path.exists(c):
            return c
    return ""


def to_wav(src, dst):
    if os.path.splitext(src)[1].lower() == ".wav":
        shutil.copy2(src, dst)
        return True
    exe = ffmpeg()
    if not exe:
        return False
    try:
        subprocess.run([exe, "-y", "-v", "error", "-i", src, "-ar", "48000", "-ac", "1",
                        "-c:a", "pcm_s16le", dst], check=True)
        return os.path.exists(dst)
    except Exception:
        return False


def unique_name(used, text):
    base = re.sub(r'[\\/:*?"<>|]', "_", text).strip() or "voice"
    name, i = base, 2
    while name in used:
        name = "%s_%d" % (base, i)
        i += 1
    used.add(name)
    return name


def write_voices(src_dir, d, used):
    """把用户给的语音文件夹收进 音效/voices/，返回 (行, 第一条 wav 相对路径)。"""
    rows, first = [], ""
    if not src_dir or not os.path.isdir(src_dir):
        return rows, first
    vdir = os.path.join(d, VOICE_DIR)
    os.makedirs(vdir, exist_ok=True)
    for f in sorted(os.listdir(src_dir)):
        src = os.path.join(src_dir, f)
        if not os.path.isfile(src) or os.path.splitext(f)[1].lower() not in AUDIO_EXT:
            continue
        text = os.path.splitext(f)[0].strip()
        if not text:
            continue
        nm = unique_name(used, text)
        dst = os.path.join(vdir, nm + ".wav")
        if to_wav(src, dst):
            rp = rel(os.path.join(VOICE_DIR, nm + ".wav"))
            rows.append((text, rp))
            if not first:
                first = rp
    return rows, first


AMP_KEYS = {"idle": "ampIdle", "press": "ampPress", "click": "ampClick", "drag": "ampDrag"}


def read_amps(d):
    m = read_manifest(d)
    amps = {}
    for k, key in AMP_KEYS.items():
        try:
            amps[k] = max(0.0, float(m.get(key, "1") or 1))
        except ValueError:
            amps[k] = 1.0
    return amps


def build_frames(d, char_h):
    image = find_image(d)
    if not image:
        raise SystemExit("没有 图片/image.*，先生成不了动画")
    sys.path.insert(0, HERE)
    import sprites
    sprites.build(image, int(char_h), os.path.join(d, ANIM_DIR), read_amps(d))


def new_pack(name, image, voices, lines, press, release, char_h, force=False, amps=None):
    amps = amps or {}
    name = safe_name(name)
    if not name:
        raise SystemExit("角色名不能为空")
    d = pack_dir(name)
    if os.path.exists(d) and not force:
        raise SystemExit("角色已存在：%s（加 --force 覆盖）" % d)
    for sub in (IMG_DIR, ANIM_DIR, VOICE_DIR):
        os.makedirs(os.path.join(d, sub), exist_ok=True)

    if image:
        ext = os.path.splitext(image)[1].lower() or ".png"
        shutil.copy2(image, os.path.join(d, IMG_DIR, "image" + ext))
    elif not find_image(d):
        raise SystemExit("请用 --image 指定立绘")

    used = set()
    rows, first_voice = write_voices(voices, d, used)
    tsv = os.path.join(d, SFX_DIR, "voices.tsv")
    with open(tsv, "w", encoding="utf-8", newline="") as fh:
        for text, rp in rows:
            fh.write(text + "\t" + rp + "\r\n")

    if lines:
        shutil.copy2(lines, os.path.join(d, "台词.txt"))
    else:
        with open(os.path.join(d, "台词.txt"), "w", encoding="utf-8", newline="") as fh:
            for text, _ in rows:
                fh.write(text + "\r\n")

    data = {
        "name": name,
        "image": rel(os.path.join(IMG_DIR, os.path.basename(find_image(d)))),
        "frames": rel(os.path.join(ANIM_DIR, "frames.bin")),
        "meta": rel(os.path.join(ANIM_DIR, "frames.txt")),
        "lines": "台词.txt",
        "voicelines": rel(os.path.join(SFX_DIR, "voices.tsv")),
        "charH": str(int(char_h)),
        "ampIdle": "%.2f" % float(amps.get("idle", 1)),
        "ampPress": "%.2f" % float(amps.get("press", 1)),
        "ampClick": "%.2f" % float(amps.get("click", 1)),
        "ampDrag": "%.2f" % float(amps.get("drag", 1)),
        "voices": str(len(rows)),
    }
    if press:
        shutil.copy2(press, os.path.join(d, SFX_DIR, "press.wav"))
    if release:
        shutil.copy2(release, os.path.join(d, SFX_DIR, "release.wav"))
    if os.path.exists(os.path.join(d, SFX_DIR, "press.wav")):
        data["press"] = rel(os.path.join(SFX_DIR, "press.wav"))
    elif first_voice:
        data["press"] = first_voice          # 没单独给按压音就用第一条语音兜底
    if os.path.exists(os.path.join(d, SFX_DIR, "release.wav")):
        data["release"] = rel(os.path.join(SFX_DIR, "release.wav"))
    elif len(rows) > 1:
        data["release"] = rows[1][1]

    write_manifest(d, data)
    build_frames(d, char_h)
    print("角色包已创建：%s" % d)
    print("  图片 %s" % data["image"])
    print("  动画 %s（%s px）" % (data["frames"], data["charH"]))
    print("  音效 %d 条语音 → %s" % (len(rows), data["voicelines"]))
    return d


def rebuild(name, char_h):
    d = pack_dir(name)
    if not os.path.isdir(d):
        raise SystemExit("找不到角色：%s" % d)
    data = read_manifest(d)
    build_frames(d, char_h)
    data["charH"] = str(int(char_h))
    write_manifest(d, data)
    print("已重建动画：%s（%s px）" % (d, char_h))


def list_packs():
    if not os.path.isdir(CHARS):
        print("还没有 chars 目录")
        return
    for d in sorted(os.listdir(CHARS)):
        p = os.path.join(CHARS, d)
        if not os.path.isdir(p):
            continue
        m = read_manifest(p)
        ok_anim = os.path.exists(os.path.join(p, ANIM_DIR, "frames.bin"))
        print("%-10s 语音 %-3s 大小 %-4s 动画 %s%s"
              % (m.get("name", d), m.get("voices", "0"), m.get("charH", "?"),
                 "✓" if ok_anim else "✗", "" if m else "  (缺少 char.json)"))


def main():
    ap = argparse.ArgumentParser(description="角色包管理")
    sub = ap.add_subparsers(dest="cmd")

    p = sub.add_parser("list", help="列出所有角色")
    p = sub.add_parser("new", help="新建角色（新建文件夹 + 传入图片/语音/台词）")
    p.add_argument("--name", required=True)
    p.add_argument("--image", default="")
    p.add_argument("--voices", default="", help="语音文件夹（文件名=台词）")
    p.add_argument("--lines", default="", help="台词.txt（不给就用语音文件名）")
    p.add_argument("--press", default="")
    p.add_argument("--release", default="")
    p.add_argument("--char-h", type=int, default=DEFAULT_H)
    p.add_argument("--amp-idle", type=float, default=1.0)
    p.add_argument("--amp-press", type=float, default=1.0)
    p.add_argument("--amp-click", type=float, default=1.0)
    p.add_argument("--amp-drag", type=float, default=1.0)
    p.add_argument("--force", action="store_true")

    p = sub.add_parser("rebuild", help="重新生成某个角色的动画")
    p.add_argument("--name", required=True)
    p.add_argument("--char-h", type=int, default=0)

    a = ap.parse_args()
    if a.cmd == "list":
        list_packs()
    elif a.cmd == "new":
        new_pack(a.name, a.image, a.voices, a.lines, a.press, a.release, a.char_h, a.force,
                 {"idle": a.amp_idle, "press": a.amp_press, "click": a.amp_click, "drag": a.amp_drag})
    elif a.cmd == "rebuild":
        h = a.char_h or int(read_manifest(a.name).get("charH", DEFAULT_H))
        rebuild(a.name, h)
    else:
        ap.print_help()


if __name__ == "__main__":
    main()
