// ============================================================================
// Q Pet · 与 UI / Win32 无关的纯函数
// ----------------------------------------------------------------------------
// 为什么单独一个文件：这些规则以前散落在 pet.cs 与 Settings.cs 各处，改错了只能靠
// 运行时自检（--selftest）和肉眼发现。抽出来之后：
//   ① Tests.csproj 直接对它们跑断言（编译期之外唯一能自动发现回归的地方）；
//   ② Pet.exe 与 PetSettings.exe 用同一份实现，不会出现「两边格式不一致」——
//      之前 FmtSec 就是两份一模一样的拷贝。
// 约束：这个文件不许引用 System.Drawing / WPF / 任何 P-Invoke，保持纯逻辑。
// ============================================================================
using System;
using System.IO;
using System.Text;

public static class QCore
{
    // ---------------- 时长 ----------------
    public static string FmtSec(int s)
    {
        if (s < 60) return s + " 秒";
        if (s < 3600) return (s / 60) + " 分 " + (s % 60) + " 秒";
        return (s / 3600) + " 小时 " + ((s % 3600) / 60) + " 分";
    }

    public static string FmtShort(int s)
    {
        if (s <= 0) return "—";
        if (s < 60) return s + "秒";
        if (s < 3600) return (s / 60) + "分";
        return (s / 3600) + "时" + ((s % 3600) / 60).ToString("00") + "分";
    }

    // ---------------- 杂项 ----------------
    // FNV-1a 32 位：只用来给「源语音 + 音量」生成缓存文件名，不用于任何安全用途
    public static string FnvHash(string s)
    {
        uint h = 2166136261;
        for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
        return h.ToString("x8");
    }

    public static int ClampInt(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

    public static double ClampD(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

    // 角色包目录名：去掉 Windows 非法字符、去首尾空白与结尾的点、限长 40
    public static string SanitizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s.Trim())
        {
            if ("\\/:*?\"<>|".IndexOf(c) >= 0) continue;
            sb.Append(c);
        }
        string r = sb.ToString().Trim().TrimEnd('.');
        return r.Length > 40 ? r.Substring(0, 40) : r;
    }

    public static string SafeFile(string s)
    {
        if (s == null) return "";
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    // ---------------- 台词清单（voices.tsv） ----------------
    // 每行「台词<TAB>音频路径」，路径可相对角色包目录。
    // 改名 / 删除只定位到匹配的那一行，其它行（包括认不出的行）原样保留。
    public static int FindVoiceLine(string[] lines, string text, string audioPath, string baseDir)
    {
        if (lines == null || string.IsNullOrEmpty(audioPath)) return -1;
        for (int i = 0; i < lines.Length; i++)
        {
            int tab = lines[i].IndexOf('\t');
            if (tab <= 0) continue;
            string t = lines[i].Substring(0, tab).Trim();
            if (t != (text ?? "")) continue;
            string p = lines[i].Substring(tab + 1).Trim();
            if (p == "") continue;
            string full = Path.IsPathRooted(p) ? p : Path.Combine(baseDir ?? "", p);
            try
            {
                if (string.Equals(Path.GetFullPath(full), Path.GetFullPath(audioPath),
                    StringComparison.OrdinalIgnoreCase)) return i;
            }
            catch (Exception) { }
        }
        return -1;
    }

    public static string RenameVoiceLine(string line, string text)
    {
        if (string.IsNullOrEmpty(line)) return line;
        int tab = line.IndexOf('\t');
        if (tab <= 0) return line;
        return (text ?? "") + line.Substring(tab);
    }

    // ---------------- CSV（RFC 4180）----------------
    public static string CsvCell(string s)
    {
        if (s == null) return "";
        if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    public static string CsvRow(params string[] cells)
    {
        var arr = new string[cells.Length];
        for (int i = 0; i < cells.Length; i++) arr[i] = CsvCell(cells[i]);
        return string.Join(",", arr);
    }
}
