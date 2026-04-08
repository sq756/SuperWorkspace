using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // ==========================================
        // 模块 1：底层 ADB 通信基础方法
        // ==========================================

        private string RunAdbCommand(string arguments)
        {
            try
            {
                // 🌟 核心修复：不要依赖系统的环境变量！直接从 scrcpy 目录下精准抓取 adb.exe
                string adbPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scrcpyPath) ?? "", "adb.exe");
                if (!System.IO.File.Exists(adbPath)) adbPath = "adb"; // 兜底防崩

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(psi)!)
                {
                    string output = process.StandardOutput.ReadToEnd() ?? "";
                    process.WaitForExit();
                    return output;
                }
            }
            catch { return ""; }
        }

        private string CheckDeviceStatus()
        {
            string output = RunAdbCommand("devices");
            if (output.Contains("unauthorized")) return "unauthorized";
            if (output.Contains("\tdevice")) return "connected";
            return "disconnected";
        }

        // 🌟 核心魔法：自动从 Android 系统底层“偷”出局域网 IP
        private async Task<string> StealDeviceIpAsync(string deviceId)
        {
            string output = await Task.Run(() => RunAdbCommand($"-s {deviceId} shell ip route"));
            string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("src") && (line.Contains("wlan0") || line.Contains("eth0")))
                {
                    string[] parts = line.Split(' ');
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] == "src" && i + 1 < parts.Length)
                        {
                            string ip = parts[i + 1].Trim();
                            if (ip.StartsWith("192.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                                return ip;
                        }
                    }
                }
            }
            return "";
        }

        // 🌟 组合方案一的精髓：全自动转换为无线模式
        private async void Btn_MakeWireless_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDevice == null || _selectedDevice.IsWireless)
            {
                ShowCyberMessage("⚠️ 操作无效", "当前未选中设备，或设备已经是无线状态！");
                return;
            }

            StatusText.Text = "状态: 🪄 正在施展无线魔法，请勿拔除数据线...";

            try
            {
                string deviceId = _selectedDevice.Id;
                string ip = await StealDeviceIpAsync(deviceId);
                if (string.IsNullOrEmpty(ip))
                {
                    ShowCyberMessage("⚠️ IP 获取失败", "未能获取到平板的 Wi-Fi IP，请确保平板已连接与电脑相同的路由器！");
                    return;
                }

                await Task.Run(() => RunAdbCommand($"-s {deviceId} tcpip 5555"));
                await Task.Delay(2000); 
                string connectResult = await Task.Run(() => RunAdbCommand($"connect {ip}:5555"));

                if (connectResult.Contains("connected"))
                {
                    ShowCyberMessage("🪄 魔法成功", $"✅ 无线通道 ({ip}) 已成功建立！\n\n现在你可以安全地拔掉数据线了，然后点击左侧的【扩展电脑副屏】或【主屏接管】即可起飞！");
                    await RefreshDeviceList();
                }
                else
                {
                    ShowCyberMessage("⚠️ 连接超时", "连接超时，请检查路由器防火墙设置！\n" + connectResult);
                }
            }
            catch (Exception ex)
            {
                ShowCyberMessage("❌ 魔法施展失败", ex.Message);
            }
        }

        private async void Btn_RefreshDevices_Click(object sender, RoutedEventArgs e) => await RefreshDeviceList();

        private async Task RefreshDeviceList()
        {
            ComboDeviceList.Items.Clear();
            _activeDevices.Clear();
            StatusText.Text = "状态: 正在扫描周边设备...";

            string output = await Task.Run(() => RunAdbCommand("devices -l"));
            string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.Contains("device ") || line.Contains("device\t")) 
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    string id = parts[0];
                    bool isWireless = id.Contains(":") && id.Contains("."); 
                    string modelName = isWireless ? "📶 无线设备" : "🔌 USB 设备";
                    foreach (var p in parts) if (p.StartsWith("model:")) modelName += " [" + p.Substring(6) + "]";

                    var device = new AdbDevice { Id = id, Model = modelName, Status = "device", IsWireless = isWireless };
                    _activeDevices.Add(device);
                    ComboDeviceList.Items.Add(device); 
                }
            }

            if (ComboDeviceList.Items.Count > 0) {
                AppendLog($"Scanner: Found {_activeDevices.Count} active devices.");
                ComboDeviceList.SelectedIndex = 0;
                StatusText.Text = $"状态: ✅ 扫描完成，发现 {_activeDevices.Count} 台设备";
            } else {
                _selectedDevice = null;
                StatusText.Text = "状态: ⚠️ 未发现任何设备，请检查连接";
                SetDormantState(); // 🌟 设备断开时设置沉睡样式
            }
        }

        private void ComboDeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboDeviceList.SelectedItem is AdbDevice device)
            {
                _selectedDevice = device;
                if (monitorTimer.IsEnabled) MonitorTimer_Tick(null, null); 
            }
        }

        // ==========================================
        // 🌟 终极排雷：强制重启底层 ADB 引擎，解决多端冲突与 RSA 授权罢工
        // ==========================================
        private async void Btn_FixAdb_Click(object sender, RoutedEventArgs e)
        {
            AboutOverlay.Visibility = Visibility.Collapsed; // 🌟 体验优化：点击后自动关闭设置面板，方便用户看弹窗
            StatusText.Text = "状态: 🔌 正在暴力重启底层通信引擎...";
            try {
                await Task.Run(() => {
                    // 1. 终极猎杀：连同各大手机助手（荣耀、华为、360等）的流氓拦截器一起物理超度！
                    string[] toxicProcesses = { "adb", "hdb", "sjadb", "tadb", "kdb", "MobileMgr", "kadb" };
                    foreach (var name in toxicProcesses) {
                        foreach (var p in Process.GetProcessesByName(name)) { try { p.Kill(); } catch { } }
                    }
                    
                    // 2. 补刀：显式调用 kill-server，彻底摧毁可能残留的僵尸端口锁
                    RunAdbCommand("kill-server");
                    
                    // 3. 重新拉起干净的 ADB Server
                    RunAdbCommand("start-server");
                    
                    // 4. 🌟 终极绝杀：强行要求内核轮询一次 USB 端口！这 100% 会向手机下发 RSA 握手密钥！
                    RunAdbCommand("devices");
                });
                
                ShowCyberMessage("🔌 通信引擎已重启", "底层的 ADB 服务已强行重置！\n\n👉 此时如果您插着数据线，手机屏幕上应该会立刻弹出【是否允许 USB 调试】的 RSA 授权确认框。\n请务必勾选【始终允许】并点击确定后，再次点击左侧的刷新列表按钮。");
            } catch (Exception ex) { ShowCyberMessage("❌ 重启失败", "重启 ADB 服务时发生错误：\n" + ex.Message); }
        }
    }
}