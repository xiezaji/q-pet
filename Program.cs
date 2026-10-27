// ============================================================================
// Q Pet —— 独立程序入口
// 编译：dotnet build Pet.csproj -c Release（产物 build\pet\Pet.exe）
//       旧方式：csc /target:winexe /out:Pet.exe pet.cs Program.cs /r:System.dll,System.Drawing.dll
// 运行：双击 Pet.exe（不需要 PowerShell、不需要 DeepSeek Harness）
// ============================================================================
using System;
using System.IO;
using System.Threading;

public static class PetMain
{
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
                // 以前直接 return ⇒ 用户双击两次毫无反应，还查不到原因。
                // 现在：请已运行的那个实例把设置面板打开，并在 pet-status.txt 里留一条。
                PetApp.Pet.NotifyExistingInstance(dir);
                return;
            }
            try { File.WriteAllText(Path.Combine(dir, "pet.pid"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString()); }
            catch (Exception) { }
            try { PetApp.Pet.Run(cfg); }
            catch (Exception ex)
            {
                // 以前这里把异常吞了 ⇒ 双击没反应也查不到原因。现在落一份 crash 日志。
                try { File.WriteAllText(Path.Combine(dir, "pet-crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex); }
                catch (Exception) { }
            }
            try { File.Delete(Path.Combine(dir, "pet.pid")); }
            catch (Exception) { }
        }
    }
}
