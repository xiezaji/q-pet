"""XAML <-> C# verbatim string 往返工具。

用法：
  python tools/xamlgen.py export  # src/Settings.cs -> _ref/xaml.xaml（反解 "" -> "）
  python tools/xamlgen.py inject  # _ref/xaml.xaml -> src/Settings.cs（转义 " -> ""）

为何要这么绕：XAML 写在 C# verbatim 字符串里，字符串内不能出现半角双引号，
历史上因此连炸 5 次（编译永远 0 错误，只有运行时解析才炸）。
把 XAML 放独立文件编辑 + 脚本转义，才能真正看见和校验它。
"""
import io
import os
import sys
import re
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CS = os.path.join(ROOT, "src", "Settings.cs")
XAML = os.path.join(ROOT, "_ref", "xaml.xaml")

MARK_START = 'const string XamlText = @"'
MARK_END = '";\n}'


def read_cs():
    with io.open(CS, "r", encoding="utf-8-sig", newline="") as f:
        return f.read()


def write_cs(text):
    # 保留原文件是否有 BOM
    with io.open(CS, "rb") as f:
        raw = f.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    with io.open(CS, "w", encoding="utf-8-sig" if bom else "utf-8", newline="") as f:
        f.write(text)
    return bom


def span(src):
    i = src.index(MARK_START)
    body_start = i + len(MARK_START)
    # 行尾可能是 CRLF；锚定「";」+ 换行 + 顶格 }
    m = re.compile(r'";[ \t]*\r?\n\}').search(src, body_start)
    if not m:
        raise SystemExit("找不到 XamlText 结尾标记")
    return body_start, m.start()


def export():
    src = read_cs()
    a, b = span(src)
    body = src[a:b]
    # verbatim string: "" -> "
    out = body.replace('""', '"')
    with io.open(XAML, "w", encoding="utf-8", newline="") as f:
        f.write(out)
    check(out)
    print("export -> %s (%d chars, %d lines)" % (XAML, len(out), out.count("\n") + 1))


def check(xaml_text):
    try:
        ET.fromstring(xaml_text)
    except ET.ParseError as e:
        print("XML PARSE ERROR:", e)
        line, col = e.position
        print("  at line %d col %d" % (line, col))
        lines = xaml_text.split("\n")
        for n in range(max(0, line - 4), min(len(lines), line + 1)):
            print("  %5d | %s" % (n + 1, lines[n][:170]))
        return False
    bad = re.findall(r'="[^"]*"', xaml_text)
    print("XML OK, %d attribute values" % len(bad))
    return True


def verify():
    """校验 Settings.cs 里**当前**那段 XAML（不写任何文件）。

    build.cmd 每次构建都会调它：编译 0 错误 ≠ XAML 能解析，历史上因为运行时
    XamlReader.Parse 抛异常炸过好几次，而编译器对此一声不吭。
    """
    src = read_cs()
    a, b = span(src)
    if a < 0 or b < 0:
        print("XAML CHECK: 没找到 XamlText 的起止标记，跳过")
        return 0
    body = src[a:b].replace('""', '"')
    if check(body):
        print("XAML CHECK OK")
        return 0
    print("XAML CHECK FAILED")
    return 1


def inject():
    with io.open(XAML, "r", encoding="utf-8") as f:
        xaml_text = f.read()
    if not check(xaml_text):
        print("ABORT: 未通过 XML 校验，不写回 Settings.cs")
        sys.exit(1)
    body = xaml_text.replace('"', '""')
    src = read_cs()
    a, b = span(src)
    new = src[:a] + body + src[b:]
    bom = write_cs(new)
    print("inject -> src/Settings.cs (%s)" % ("BOM" if bom else "no BOM"))


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "export"
    if cmd == "export":
        export()
    elif cmd == "inject":
        inject()
    elif cmd == "check":
        sys.exit(verify())
    else:
        print(__doc__)
