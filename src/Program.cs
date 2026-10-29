// ============================================================================
// Q Pet —— 独立程序入口
// 编译：dotnet build src/Pet.csproj -c Release（产物 build\pet\Pet.exe）
//       旧方式：csc /target:winexe /out:Pet.exe pet.cs Program.cs /r:System.dll,System.Drawing.dll
// 运行：双击 Pet.exe（不需要 PowerShell、不需要 DeepSeek Harness）
// ============================================================================
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class PetMain
{
    // 用 user32 的 MessageBoxW 而不是 System.Windows.Forms：Pet.exe 一直只引 System.Drawing，
    // 不为了弹一句话把整个 WinForms 拉进来。
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    const uint MB_OK = 0x0, MB_ICONWARNING = 0x30, MB_TOPMOST = 0x40000;

    [STAThread]
    public static void Main(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        if (dir.EndsWith("\\")) dir = dir.Substring(0, dir.Length - 1);

        // 配置默认在 exe 同目录；也可以把配置路径作为第一个参数传进来
        string cfg = Path.Combine(dir, "pet-config.txt");
        if (args != null && args.Length > 0 && File.Exists(args[0])) cfg = args[0];

        // 单实例：双击多次不会开出第二只；与旧启动器共用同一个命名互斥体
        bool created = false;
        using (Mutex m = new Mutex(true, "Local\\PetDesktop", out created))
        {
            if (!created)
            {
                // 双击第二次时不能静默返回：请已运行的实例打开设置面板，并留一条状态。
                PetApp.Pet.NotifyExistingInstance(dir);
                return;
            }
            try { File.WriteAllText(Path.Combine(dir, "pet.pid"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString()); }
            catch (Exception) { }
            try
            {
                EnsureConfig(cfg, dir);          // 配置丢了就现补一份：不再「双击没反应」
                PetApp.Pet.Run(cfg);
            }
            catch (Exception ex)
            {
                // 异常不能只落盘：双击没反应时没人会去翻 pet-crash.log，
                // 所以既写日志，也弹窗说明原因和下一步。
                string log = Path.Combine(dir, "pet-crash.log");
                try { File.WriteAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex, new UTF8Encoding(false)); }
                catch (Exception) { }
                try
                {
                    MessageBoxW(IntPtr.Zero,
                        "桌宠没能启动：\r\n\r\n" + ex.Message
                        + "\r\n\r\n详情见日志：" + log
                        + "\r\n\r\n修好后重新双击 Pet.exe；也可以先打开 PetSettings.exe 检查设置。",
                        "Q Pet", MB_OK | MB_ICONWARNING | MB_TOPMOST);
                }
                catch (Exception) { }
            }
            try { File.Delete(Path.Combine(dir, "pet.pid")); }
            catch (Exception) { }
        }
    }

    // 首次运行（或用户把 pet-config.txt 删了）：按 chars 目录里第一个可用角色现补一份。
    // 缺 pet-config.txt 时若不补，meta 为空会直接抛异常。
    static void EnsureConfig(string cfg, string dir)
    {
        if (File.Exists(cfg)) return;
        string chars = Path.Combine(dir, "chars");
        string pick = "";
        if (Directory.Exists(chars))
        {
            string[] subs = Directory.GetDirectories(chars);
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
            foreach (string d in subs)
                if (File.Exists(Path.Combine(d, "char.json"))) { pick = d; break; }
        }
        if (pick == "")
            throw new Exception("还没有任何角色包：chars 目录里找不到带 char.json 的角色。"
                + "\r\n\r\n请打开 PetSettings.exe，在「导入」页新建一个角色。");

        var sb = new StringBuilder();
        sb.AppendLine("# Q Pet 配置 —— 首次运行时自动生成");
        sb.AppendLine("# 可调项见 make-pet-config.ps1；改完设置面板会自动写回这里。");
        sb.AppendLine("charDir=chars\\" + Path.GetFileName(pick));
        sb.AppendLine("showPet=1");
        File.WriteAllText(cfg, sb.ToString(), new UTF8Encoding(false));
    }
}
