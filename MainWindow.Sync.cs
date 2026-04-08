using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Clipboard = System.Windows.Clipboard;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // ==========================================
        // 模块 8：功能 7 (无缝文件穿越 - 剪贴板引擎)
        // ==========================================

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private bool _isSyncingClipboard = false; // 🌟 架构师级防抖锁：阻断 PC->平板->PC 的死循环
        private List<string> _clipHistory = new List<string>(); // 🌟 离线缓存池：保存最近 20 条历史
        private WebSocket? _deckWebSocket;        // 🌟 复用控制台的全双工管道

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);

        private void InitializeSyncEngine()
        {
            WindowInteropHelper wih = new WindowInteropHelper(this);
            AddClipboardFormatListener(wih.Handle);
            HwndSource.FromHwnd(wih.Handle)?.AddHook(ClipboardHook);
        }

        private void CleanupSyncEngine()
        {
            WindowInteropHelper wih = new WindowInteropHelper(this);
            RemoveClipboardFormatListener(wih.Handle);
        }

        private string SafeGetClipboardText()
        {
            for (int i = 0; i < 5; i++) {
                try { if (Clipboard.ContainsText()) return Clipboard.GetText(); return ""; }
                catch (Exception) { Thread.Sleep(50); } // 🌟 退避重试，防 COM 死锁
            }
            return "";
        }

        private void SafeSetClipboardText(string text)
        {
            for (int i = 0; i < 5; i++) {
                try { Clipboard.SetText(text); return; }
                catch (Exception) { Thread.Sleep(50); }
            }
        }

        // 🌟 核心钩子：事件驱动的 Windows 剪贴板变动捕获
        private IntPtr ClipboardHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE && !_isSyncingClipboard)
            {
                string newText = SafeGetClipboardText();
                if (!string.IsNullOrWhiteSpace(newText))
                {

                    // 🌟 缓存入库：去重并放在最前面
                    _clipHistory.RemoveAll(x => x == newText);
                    _clipHistory.Insert(0, newText);
                    if (_clipHistory.Count > 20) _clipHistory.RemoveAt(20);

                    PushClipboardToTablet(newText);
                    SyncHistoryToTablet(); // 触发全量历史更新
                }
            }
            return IntPtr.Zero;
        }

        private async void PushClipboardToTablet(string text)
        {
            if (_deckWebSocket != null && _deckWebSocket.State == WebSocketState.Open)
            {
                var payload = new { type = "CLIPBOARD_SYNC", content = text };
                string json = JsonSerializer.Serialize(payload);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                await _deckWebSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                Dispatcher.Invoke(() => StatusText.Text = $"状态: 📋 剪贴板已推送到平板 ({text.Length} 字符)");
            }
            else
            {
                Dispatcher.Invoke(() => StatusText.Text = $"状态: 📋 平板离线，剪贴板已存入本地缓存 ({text.Length} 字符)");
            }
        }

        // 🌟 将历史记录池同步给平板
        private async void SyncHistoryToTablet()
        {
            if (_deckWebSocket != null && _deckWebSocket.State == WebSocketState.Open)
            {
                var payload = new { type = "CLIPBOARD_HISTORY", content = _clipHistory };
                byte[] buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                await _deckWebSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        // 🌟 接收平板传回的剪贴板并写入 PC
        private void UpdatePcClipboard(string text)
        {
            _isSyncingClipboard = true; // ⚠️ 上锁：我正在写入，请钩子忽略下一次变更
            _clipHistory.RemoveAll(x => x == text); _clipHistory.Insert(0, text); if (_clipHistory.Count > 20) _clipHistory.RemoveAt(20);
            Dispatcher.Invoke(() => { SafeSetClipboardText(text); StatusText.Text = $"状态: 📋 收到平板发来的剪贴板 ({text.Length} 字符)"; });
            SyncHistoryToTablet(); // 让历史面板也能看到平板发来的内容
            Task.Delay(500).ContinueWith(_ => _isSyncingClipboard = false); // 延时解锁
        }

        // ==========================================
        // 🌟 键鼠无缝穿越引擎 (Edge Crossing)
        // ==========================================

        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const uint LWA_ALPHA = 0x00000002;
        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;

        private System.Diagnostics.Process? _edgeProcess;
        private IntPtr _edgeHwnd = IntPtr.Zero;
        private System.Windows.Threading.DispatcherTimer? _edgeTimer;
        private bool _isMouseOnTablet = false;

        private async void Btn_StartEdgeCrossing_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            
            // 🌟 再次点击时，关闭结界
            if (_edgeProcess != null && !_edgeProcess.HasExited)
            {
                try { _edgeProcess.Kill(); } catch {}
                _edgeTimer?.Stop();
                _isMouseOnTablet = false;
                _edgeHwnd = IntPtr.Zero;
                StatusText.Text = "状态: 🔮 键鼠穿越已关闭";
                Card_ZeroSync.Tag = "";
                if (btn != null) btn.Content = "🔮 开启边缘键鼠穿越 (无线 Flow)";
                return;
            }

            // 🌟 首次点击，启动结界
            if (_selectedDevice == null) { ShowCyberMessage("⚠️ 未选择设备", "请先在右侧选择设备！"); return; }
            if (btn != null) btn.Content = "🛑 关闭边缘键鼠穿越";
            
            ZeroSyncOverlay.Visibility = Visibility.Collapsed;
            StatusText.Text = "状态: 🔮 正在召唤透明键鼠结界...";

            try {
                string targetId = _selectedDevice.Id;

                // 🌟 使用超低功耗参数启动 Scrcpy，仅用于获取窗口与接收键鼠
                // -m 480 -b 500K --max-fps 15 (极限省电)
                // --keyboard=uhid --mouse=uhid (开启原生键鼠体验)
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = scrcpyPath,
                    Arguments = $"-s {targetId} --window-title=SW_EdgeCross --keyboard=uhid --mouse=uhid --window-borderless -m 480 -b 500K --max-fps 15",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                _edgeProcess = System.Diagnostics.Process.Start(psi);

                // 等待结界窗口产生
                int retry = 0;
                while (_edgeHwnd == IntPtr.Zero && retry < 50) {
                    await Task.Delay(100);
                    _edgeHwnd = FindWindow(null, "SW_EdgeCross");
                    retry++;
                }

                if (_edgeHwnd == IntPtr.Zero) {
                    ShowCyberMessage("❌ 启动失败", "未能捕获到结界窗口！请确保设备支持 uhid 模式。");
                    if (btn != null) btn.Content = "🔮 开启边缘键鼠穿越 (无线 Flow)";
                    return;
                }

                // 🌟 核心魔法：将结界变成 1% 透明的隐身状态！
                int style = GetWindowLong(_edgeHwnd, GWL_EXSTYLE);
                SetWindowLong(_edgeHwnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
                SetLayeredWindowAttributes(_edgeHwnd, 0, 2, LWA_ALPHA); 

                ShowWindow(_edgeHwnd, SW_HIDE); // 初始隐藏

                // 🌟 启动高频探针雷达，探测鼠标是否撞击边缘
                _edgeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                _edgeTimer.Tick += EdgeTimer_Tick;
                _edgeTimer.Start();

                ShowCyberMessage("🔮 键鼠结界部署完毕", "结界已在后台待命！\n\n👉 请将电脑鼠标【一直向右滑】，撞击主屏幕最右侧边缘，鼠标将瞬间穿越到平板内部！\n👉 在平板中向左滑撞击边缘，即可瞬间回到电脑！\n\n注意：穿越期间如果误按 Alt+Tab 切走焦点，鼠标也会自动弹回电脑。");
                Card_ZeroSync.Tag = "Active";

            } catch (Exception ex) { ShowCyberMessage("❌ 启动异常", ex.Message); }
        }

        private void EdgeTimer_Tick(object? sender, EventArgs e)
        {
            if (_edgeHwnd == IntPtr.Zero) return;
            GetCursorPos(out POINT pt);
            int screenW = (int)SystemParameters.PrimaryScreenWidth;
            int screenH = (int)SystemParameters.PrimaryScreenHeight;

            if (!_isMouseOnTablet) {
                if (pt.x >= screenW - 2) { // 🌟 撞击右边缘：穿越！
                    _isMouseOnTablet = true;
                    SetWindowPos(_edgeHwnd, new IntPtr(-1), 0, 0, screenW, screenH, 0x0040); // 覆盖全屏
                    ShowWindow(_edgeHwnd, SW_RESTORE);
                    SetForegroundWindow(_edgeHwnd);
                    SetCursorPos(10, pt.y); // 将鼠标稍微往左移一点，避免卡在结界外
                }
            } else {
                if (GetForegroundWindow() != _edgeHwnd) { // 用户 Alt+Tab 切走了，强行召回
                    _isMouseOnTablet = false; ShowWindow(_edgeHwnd, SW_HIDE); return;
                }
                if (pt.x <= 2) { // 🌟 撞击左边缘：回归！
                    _isMouseOnTablet = false;
                    ShowWindow(_edgeHwnd, SW_HIDE);
                    SetCursorPos(screenW - 20, pt.y); // 弹出到电脑右侧
                }
            }
        }
    }
}