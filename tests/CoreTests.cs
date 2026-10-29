// ============================================================================
// Q Pet · 纯函数单测
// ----------------------------------------------------------------------------
// 为什么要这个工程：--selftest 是「把界面跑起来看控件状态」，它发现不了纯逻辑回归
// （比如时长格式、CSV 转义、角色名清洗）。这些规则现在集中在 Core.cs，
// 这里对它们跑断言，build.cmd 每次构建都会执行。
// 不引入 NUnit/xUnit：net48 + 离线环境，零 NuGet 依赖最稳，退出码非 0 即失败。
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class Tests
{
    static int _pass = 0;
    static readonly List<string> _fail = new List<string>();

    static void Eq(string what, string expected, string actual)
    {
        if (expected == actual) { _pass++; return; }
        _fail.Add(what + "\n        期望: [" + expected + "]\n        实际: [" + actual + "]");
    }

    static void True(string what, bool cond)
    {
        if (cond) { _pass++; return; }
        _fail.Add(what + " 期望为真，实际为假");
    }

    public static int Main()
    {
        // ---- 时长 ----
        Eq("FmtSec(0)", "0 秒", QCore.FmtSec(0));
        Eq("FmtSec(59)", "59 秒", QCore.FmtSec(59));
        Eq("FmtSec(60)", "1 分 0 秒", QCore.FmtSec(60));
        Eq("FmtSec(61)", "1 分 1 秒", QCore.FmtSec(61));
        Eq("FmtSec(3599)", "59 分 59 秒", QCore.FmtSec(3599));
        Eq("FmtSec(3600)", "1 小时 0 分", QCore.FmtSec(3600));
        Eq("FmtSec(3661)", "1 小时 1 分", QCore.FmtSec(3661));
        Eq("FmtShort(0)", "—", QCore.FmtShort(0));
        Eq("FmtShort(-5)", "—", QCore.FmtShort(-5));
        Eq("FmtShort(45)", "45秒", QCore.FmtShort(45));
        Eq("FmtShort(90)", "1分", QCore.FmtShort(90));
        Eq("FmtShort(3725)", "1时02分", QCore.FmtShort(3725));

        // ---- 哈希 ----
        Eq("FnvHash 空串（FNV-1a 偏移常量）", "811c9dc5", QCore.FnvHash(""));
        True("FnvHash 长度 8 位十六进制", QCore.FnvHash("chars\\阿米娅").Length == 8);
        True("FnvHash 稳定", QCore.FnvHash("a.wav") == QCore.FnvHash("a.wav"));
        True("FnvHash 不同输入不同输出", QCore.FnvHash("a.wav") != QCore.FnvHash("b.wav"));

        // ---- 夹取 ----
        Eq("ClampInt 下界", "0", QCore.ClampInt(-3, 0, 100).ToString());
        Eq("ClampInt 上界", "100", QCore.ClampInt(999, 0, 100).ToString());
        Eq("ClampInt 中间", "42", QCore.ClampInt(42, 0, 100).ToString());
        Eq("ClampD 下界", "0.5", QCore.ClampD(0.1, 0.5, 2.0).ToString("0.0",
            System.Globalization.CultureInfo.InvariantCulture));

        // ---- 角色名清洗 ----
        Eq("SanitizeName 空", "", QCore.SanitizeName("   "));
        Eq("SanitizeName 去非法字符", "abc", QCore.SanitizeName("a/b:c"));
        Eq("SanitizeName 去首尾空白", "小维", QCore.SanitizeName("  小维  "));
        Eq("SanitizeName 去结尾的点", "小维", QCore.SanitizeName("小维..."));
        Eq("SanitizeName 限长 40", "40", QCore.SanitizeName(new string('名', 100)).Length.ToString());
        Eq("SanitizeName 保留中文与空格", "阿米娅 2", QCore.SanitizeName("阿米娅 2"));

        // ---- 文件名清洗 ----
        Eq("SafeFile 空", "", QCore.SafeFile(null));
        True("SafeFile 不含路径分隔符",
            QCore.SafeFile("chars/x").IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        True("SafeFile 不含非法字符",
            QCore.SafeFile("a?b*c").IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

        // ---- 台词清单 ----
        string[] vrows = { "第一句\t音效/voices/a.wav", "不成对的行", "第二句\t音效/voices/b.wav" };
        Eq("FindVoiceLine 相对路径", "0",
            QCore.FindVoiceLine(vrows, "第一句", @"C:\pack\音效\voices\a.wav", @"C:\pack").ToString());
        Eq("FindVoiceLine 第二条", "2",
            QCore.FindVoiceLine(vrows, "第二句", @"C:\pack\音效\voices\b.wav", @"C:\pack").ToString());
        Eq("FindVoiceLine 路径不符", "-1",
            QCore.FindVoiceLine(vrows, "第一句", @"C:\pack\音效\voices\b.wav", @"C:\pack").ToString());
        Eq("FindVoiceLine 跳过坏行", "-1",
            QCore.FindVoiceLine(vrows, "不成对的行", @"C:\pack\x.wav", @"C:\pack").ToString());
        Eq("RenameVoiceLine 保留路径", "改名了\t音效/voices/a.wav",
            QCore.RenameVoiceLine("第一句\t音效/voices/a.wav", "改名了"));
        Eq("RenameVoiceLine 坏行原样", "坏行", QCore.RenameVoiceLine("坏行", "改名了"));

        // ---- CSV ----
        Eq("CsvCell 普通", "小维", QCore.CsvCell("小维"));
        Eq("CsvCell 空", "", QCore.CsvCell(null));
        Eq("CsvCell 含逗号要加引号", "\"a,b\"", QCore.CsvCell("a,b"));
        Eq("CsvCell 引号翻倍", "\"a\"\"b\"", QCore.CsvCell("a\"b"));
        Eq("CsvCell 换行加引号", "\"a\nb\"", QCore.CsvCell("a\nb"));
        Eq("CsvRow 拼接", "1,2,3", QCore.CsvRow("1", "2", "3"));
        Eq("CsvRow 逐列转义", "a,\"b,c\"", QCore.CsvRow("a", "b,c"));

        // ---- 动画帧清单（角色包里的 frames.txt，属于不可信输入）----
        // 这一组是回归测试：以前 pet.cs 用 int.Parse + int 乘法，
        // 一个手改坏的 frames.txt 就能让桌宠启动即崩，或者带着负偏移去读像素。
        var good = QCore.ParseFramesMeta(new string[] {
            "w=213", "h=216", "idle=24", "press=6", "click=6", "drag=8", "fps=30" });
        True("frames: 正常清单可用", good.IsOk);
        Eq("frames: w", "213", good.W.ToString());
        Eq("frames: h", "216", good.H.ToString());
        Eq("frames: 总帧数", "44", good.Total.ToString());
        Eq("frames: 30fps → 33ms", "33", good.FrameMs.ToString());
        Eq("frames: 每帧字节", "184032", (good.W * good.H * 4).ToString());
        Eq("frames: 总字节", "8097408", good.NeedBytes.ToString());

        var noFps = QCore.ParseFramesMeta(new string[] { "w=100", "h=100", "idle=4", "press=1", "click=1", "drag=1" });
        Eq("frames: 没写 fps 默认 30 → 33ms", "33", noFps.FrameMs.ToString());
        var badFps = QCore.ParseFramesMeta(new string[] { "w=100", "h=100", "idle=4", "fps=0" });
        Eq("frames: fps=0 不能除零", "33", badFps.FrameMs.ToString());
        var fast = QCore.ParseFramesMeta(new string[] { "w=100", "h=100", "idle=4", "fps=60" });
        Eq("frames: 60fps → 17ms", "17", fast.FrameMs.ToString());

        True("frames: w 非数字要报错", !QCore.ParseFramesMeta(new string[] { "w=abc", "h=100", "idle=1" }).IsOk);
        True("frames: w 缺失要报错", !QCore.ParseFramesMeta(new string[] { "h=100", "idle=1" }).IsOk);
        True("frames: w=0 要报错", !QCore.ParseFramesMeta(new string[] { "w=0", "h=100", "idle=1" }).IsOk);
        True("frames: w=99999 超上限要报错",
            !QCore.ParseFramesMeta(new string[] { "w=99999", "h=99999", "idle=24" }).IsOk);
        True("frames: 负帧数要报错",
            !QCore.ParseFramesMeta(new string[] { "w=100", "h=100", "idle=-24" }).IsOk);
        True("frames: 一帧都没有要报错",
            !QCore.ParseFramesMeta(new string[] { "w=100", "h=100", "idle=0", "press=0" }).IsOk);
        True("frames: 空清单要报错", !QCore.ParseFramesMeta(new string[0]).IsOk);
        True("frames: null 要报错", !QCore.ParseFramesMeta(null).IsOk);
        // 4096x4096 x 44 帧 = 2.95GB：int 乘法会溢出成负数，必须走 long
        var huge = QCore.ParseFramesMeta(new string[] {
            "w=4096", "h=4096", "idle=24", "press=6", "click=6", "drag=8" });
        True("frames: 大画布可用", huge.IsOk);
        Eq("frames: 大画布总字节用 long 不溢出", "2952790016", huge.NeedBytes.ToString());

        // ---- 报告 ----
        Console.WriteLine("Q Pet 单测：通过 " + _pass + " 项，失败 " + _fail.Count + " 项");
        foreach (string f in _fail) Console.WriteLine("  ✗ " + f);
        if (_fail.Count > 0) { Console.WriteLine("TESTS FAILED"); return 1; }
        Console.WriteLine("TESTS OK");
        return 0;
    }
}
