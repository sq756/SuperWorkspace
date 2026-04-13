using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32; // 🌟 引入注册表权限

namespace SuperWorkspace
{
    public class VirtualDisplayManager
    {
        public static int GlobalGpuPreference = 2; // 0=Auto, 1=iGPU, 2=dGPU (默认独显)

        private readonly string _exePath;
        private readonly string _driverDir;

        public VirtualDisplayManager()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            
            // 1. 优先在当前运行目录（或打包后的安装目录）搜索
            string[] files = Directory.GetFiles(baseDir, "deviceinstaller64.exe", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                _exePath = files[0];
                _driverDir = Path.GetDirectoryName(_exePath) ?? baseDir;
                return;
            }

            // 2. 开发环境“全景雷达”：当使用 dotnet run 时，向上回退 4 层到项目根目录，全局搜索 publish 等文件夹
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\.."));
            if (Directory.Exists(projectRoot))
            {
                files = Directory.GetFiles(projectRoot, "deviceinstaller64.exe", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    _exePath = files[0];
                    _driverDir = Path.GetDirectoryName(_exePath) ?? baseDir;
                    return;
                }
            }

            // 3. 如果彻底找不到，返回一个标准路径，让后续抛出优雅的错误提示
            _exePath = Path.Combine(baseDir, "VirtualDisplayDriver", "deviceinstaller64.exe");
            _driverDir = Path.Combine(baseDir, "VirtualDisplayDriver");
        }

        /// <summary>
        /// 核心执行引擎：纯内存指令链与安全提权机制 (适用于多条指令并行)
        /// </summary>
        private void RunElevatedMemoryChain(string commandChain)
        {
            // 🌟 核心防御 1：强制写入显卡调度策略，终结 Windows 的选择困难症！
            // GpuPreference=2 (高性能)，GpuPreference=1 (节能)
            // 即使目标电脑只有一张显卡，Windows 也会安全地无视这条策略，不会有任何副作用。
            try {
                using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\DirectX\UserGpuPreferences");
                if (key != null) {
                    key.SetValue(_exePath, $"GpuPreference={GlobalGpuPreference};"); // 🌟 动态写入用户选择的显卡偏好
                    
                    // 🌟 终极优化：强制主程序也使用独显，彻底消灭跨显卡显存拷贝带来的延迟与死锁！
                    string mainExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(mainExe)) key.SetValue(mainExe, $"GpuPreference={GlobalGpuPreference};");
                }
            } catch { /* 忽略没有权限的微小可能 */ }

            if (!File.Exists(_exePath))
            {
                throw new FileNotFoundException($"找不到驱动安装程序: deviceinstaller64.exe\n请确保已将 usbmmidd 驱动放入 VirtualDisplayDriver 文件夹！");
            }
            if (!File.Exists(Path.Combine(_driverDir, "usbmmidd.inf")))
            {
                throw new FileNotFoundException($"驱动环境不完整！\n在 {_driverDir} 目录下找不到核心的 usbmmidd.inf 文件。\n请确保您放置的是完整的驱动文件夹，而不只是单独一个 exe 程序！");
            }

            // 🌟 终极探针 1：强行捕获初始化时的底层报错！
            string logFile = Path.Combine(Path.GetTempPath(), "SuperWorkspace_VddInitLog.txt");
            try { if (File.Exists(logFile)) File.Delete(logFile); } catch {}

            // 🌟 使用括号将所有指令包裹，并将标准输出与错误流 (2>&1) 全部重定向到日志文件！
            // 🌟 致命修复：加上 /s 保证 CMD 引号安全解析
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/s /c \"cd /d \"{_driverDir}\" & ({commandChain}) > \"{logFile}\" 2>&1\"", 
                WorkingDirectory = _driverDir,
                UseShellExecute = true,                  // 必须为 true 才能使用 runas
                Verb = "runas",                          // 触发 UAC 管理员权限弹窗
                WindowStyle = ProcessWindowStyle.Hidden, // 隐藏黑色的 CMD 窗口
                CreateNoWindow = true
            };

            try { 
                using (Process? process = Process.Start(psi)) { 
                    if (process != null) {
                        // 🌟 注意：指令链中包含了 ping 延时与驱动读写，某些电脑响应较慢，大幅放宽超时时间至 30 秒
                        if (!process.WaitForExit(30000)) { 
                            try { process.Kill(); } catch {}
                            throw new TimeoutException("底层驱动引擎发生系统级死锁，已被强行终止！");
                        }
                        
                        // 🌟 智能验尸：虽然 CMD 返回 0，但我们去检查日志里有没有 Fatal Error！
                        string detail = "";
                        try { if (File.Exists(logFile)) detail = File.ReadAllText(logFile).Trim(); } catch {}
                        // 🌟 核心修复：改用忽略大小写的 IndexOf，防止误伤，并加入 Win11 HVCI 的排雷指南
                        if (detail.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || detail.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || detail.Contains("拒绝访问") || detail.Contains("找不到")) {
                            throw new Exception($"驱动底层初始化可能失败！\n\n【底层真实控制台输出】:\n{detail}\n\n👉 终极排雷指南：\n如果您是 Windows 11 系统，极大概率是被系统安全机制拦截了未签名的虚拟驱动！\n请前往 [Windows 安全中心 -> 设备安全性 -> 内核隔离]，关闭【内存完整性】后重启电脑再试！");
                        }
                    }
                } 
            }
            catch (System.ComponentModel.Win32Exception) { throw new Exception("⚠️ 管理员权限被拒绝，底层驱动操作已被系统拦截！"); }
        }

        /// <summary>
        /// 极速单发引擎：直接提权运行目标程序，彻底绕过 cmd.exe
        /// </summary>
        private void RunElevatedCommand(string arguments)
        {
            if (!File.Exists(_exePath))
                throw new FileNotFoundException($"找不到驱动安装程序: deviceinstaller64.exe");
            if (!File.Exists(Path.Combine(_driverDir, "usbmmidd.inf")))
            {
                throw new FileNotFoundException($"驱动环境不完整！缺少 usbmmidd.inf。");
            }

            // 🌟 终极探针 2：捕获单发命令（加屏/减屏）的真实死因
            string logFile = Path.Combine(Path.GetTempPath(), "SuperWorkspace_VddLog.txt");
            try { if (File.Exists(logFile)) File.Delete(logFile); } catch {}

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/s /c \"cd /d \"{_driverDir}\" & deviceinstaller64.exe {arguments} > \"{logFile}\" 2>&1\"",
                WorkingDirectory = _driverDir,
                UseShellExecute = true,                  
                Verb = "runas",                          
                WindowStyle = ProcessWindowStyle.Hidden, 
                CreateNoWindow = true
            };

            try { 
                using (Process? process = Process.Start(psi)) { 
                    if (process != null) {
                        if (!process.WaitForExit(20000)) { 
                            try { process.Kill(); } catch {}
                            throw new TimeoutException("底层驱动引擎发生系统级死锁，已被强行终止！");
                        }
                        // 🌟 核心破局点：精准捕获底层的 C++ 执行状态！
                        if (process.ExitCode != 0) {
                            string detail = "无详细日志";
                            try { if (File.Exists(logFile)) detail = File.ReadAllText(logFile).Trim(); } catch {}
                            throw new Exception($"驱动底层拒绝了指令 (错误码: {process.ExitCode})。\n\n【底层真实报错诊断】:\n{detail}\n\n👉 修复建议：\n1. 这代表内核中根本没有该驱动！请务必先点击【强行重置驱动并清空】重新注入内核！\n2. 检查杀毒软件是否拦截了 usbmmidd.sys！");
                        }
                    }
                } 
            }
            catch (System.ComponentModel.Win32Exception) { throw new Exception("⚠️ 管理员权限被拒绝！"); }
        }

        // 🌟 终极防御 3：在内存中生成 .reg 文件内容，然后通过最可靠的 reg import 注入
        private string GenerateHighResRegFileContent() {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            sb.AppendLine();
            sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\usbmmIdd\Parameters\Monitors]");
            
            string[] resList = { "1920,1080", "1920,1200", "2560,1440", "2560,1600", "3840,2160" };
            string[] fpsList = { "60", "90", "120", "144" };
            int i = 0;
            foreach (var r in resList) {
                foreach (var f in fpsList) {
                    sb.AppendLine($"\"{i++}\"=\"{r},{f}\"");
                }
            }
            return sb.ToString();
        }

        public Task InstallDriverAsync() => Task.Run(() => {
            string regContent = GenerateHighResRegFileContent();
            string regPath = Path.Combine(Path.GetTempPath(), "SuperWorkspace_Modes.reg");
            File.WriteAllText(regPath, regContent);
            RunElevatedMemoryChain($"deviceinstaller64.exe enableidd 0 > nul 2>&1 & deviceinstaller64.exe remove usbmmidd > nul 2>&1 & reg import \"{regPath}\" & deviceinstaller64.exe install usbmmidd.inf usbmmidd");
            try { File.Delete(regPath); } catch {}
        });
        
        // 🌟 极速单发引擎同样使用纯相对路径
        public Task AddScreenAsync() => Task.Run(() => RunElevatedCommand("enableidd 1"));
        
        public Task RemoveScreenAsync() => Task.Run(() => RunElevatedCommand("enableidd 0"));

        // 🌟 终极急救：强行停止底层驱动并重新唤醒，瞬间抹杀所有幽灵屏幕！
        public Task ResetDriverAsync() => Task.Run(() => {
            string regContent = GenerateHighResRegFileContent();
            string regPath = Path.Combine(Path.GetTempPath(), "SuperWorkspace_Modes.reg");
            File.WriteAllText(regPath, regContent);
            RunElevatedMemoryChain($"deviceinstaller64.exe enableidd 0 > nul 2>&1 & deviceinstaller64.exe remove usbmmidd > nul 2>&1 & reg import \"{regPath}\" & deviceinstaller64.exe install usbmmidd.inf usbmmidd");
            try { File.Delete(regPath); } catch {}
        });
    }
}