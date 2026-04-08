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
        /// 核心执行引擎：批处理执行与静默提权机制
        /// </summary>
        private void RunBatchAsAdmin(string batchCommands)
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

            string batPath = Path.Combine(Path.GetTempPath(), "SuperWorkspace_DriverTask.bat");
            File.WriteAllText(batPath, batchCommands, System.Text.Encoding.Default); // 使用默认编码防止中文路径乱码

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = batPath,
                WorkingDirectory = _driverDir,
                UseShellExecute = true,                  // 必须为 true 才能使用 runas
                Verb = "runas",                          // 触发 UAC 管理员权限弹窗
                WindowStyle = ProcessWindowStyle.Hidden, // 隐藏黑色的 CMD 窗口
                CreateNoWindow = true
            };

            try { 
                using (Process? process = Process.Start(psi)) { 
                    if (process != null) {
                        // 注意：包含了批处理内的 ping 延时，适当放宽超时时间至 15 秒
                        if (!process.WaitForExit(15000)) { 
                            try { process.Kill(); } catch {}
                            throw new TimeoutException("底层驱动引擎发生系统级死锁，已被强行终止！");
                        }
                    }
                } 
            }
            catch (System.ComponentModel.Win32Exception) { throw new Exception("⚠️ 管理员权限被拒绝，底层驱动操作已被系统拦截！"); }
            finally { try { if (File.Exists(batPath)) File.Delete(batPath); } catch {} }
        }

        // 🌟 终极破局：生成特制 EDID 时序表（将 120Hz 基因永久打入注册表！）
        private string GenerateHighResRegistry() {
            string regPath = Path.Combine(Path.GetTempPath(), "usbmmidd_120hz.reg");
            string regContent = @"Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\usbmmIdd\Parameters\Monitors]
""0""=""1920,1080,60""
""1""=""1920,1080,120""
""2""=""1920,1200,60""
""3""=""1920,1200,120""
""4""=""2560,1440,60""
""5""=""2560,1440,120""
""6""=""2560,1600,60""
""7""=""2560,1600,120""
""8""=""3840,2160,60""
""9""=""3840,2160,120""
";
            File.WriteAllText(regPath, regContent);
            return regPath;
        }

        public Task InstallDriverAsync() => Task.Run(() => {
            string regPath = GenerateHighResRegistry();
            RunBatchAsAdmin($"cd /d \"{_driverDir}\"\r\n\"{_exePath}\" install usbmmidd.inf usbmmidd\r\nreg import \"{regPath}\"\r\n\"{_exePath}\" stop usbmmidd\r\nping 127.0.0.1 -n 2 > nul\r\n\"{_exePath}\" start usbmmidd");
        });
        
        // 🌟 恢复极速秒建！直接发送创建指令，绝不拖泥带水！
        public Task AddScreenAsync() => Task.Run(() => RunBatchAsAdmin($"cd /d \"{_driverDir}\"\r\n\"{_exePath}\" enableidd 1"));
        
        public Task RemoveScreenAsync() => Task.Run(() => RunBatchAsAdmin($"cd /d \"{_driverDir}\"\r\n\"{_exePath}\" enableidd 0"));

        // 🌟 终极急救：强行停止底层驱动并重新唤醒，瞬间抹杀所有幽灵屏幕！
        public Task ResetDriverAsync() => Task.Run(() => {
            string regPath = GenerateHighResRegistry();
            RunBatchAsAdmin($"cd /d \"{_driverDir}\"\r\nreg import \"{regPath}\"\r\n\"{_exePath}\" stop usbmmidd\r\nping 127.0.0.1 -n 2 > nul\r\n\"{_exePath}\" start usbmmidd");
        });
    }
}