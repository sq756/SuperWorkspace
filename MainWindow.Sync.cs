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
            RestorePcCursor(); // 🌟 终极保险：进程退出时，若结界尚未关闭，强制恢复系统鼠标
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
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;
        
        // 🌟 鼠标黑洞：隐身引擎核心 API
        [DllImport("user32.dll")] private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);
        [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr pcur);
        [DllImport("user32.dll")] private static extern bool SetSystemCursor(IntPtr hcur, uint id);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);
        [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr hCursor);

        private void HidePcCursor() {
            byte[] andPlane = new byte[128]; for (int i = 0; i < 128; i++) andPlane[i] = 0xFF; // 全透明掩码
            byte[] xorPlane = new byte[128]; // 全0
            IntPtr blankCursor = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andPlane, xorPlane);
            uint[] cursors = { 32512, 32513, 32649, 32650, 32515, 32646 }; // 覆盖箭头、文本、手指、加载等常见光标
            foreach (uint cursor in cursors) SetSystemCursor(CopyIcon(blankCursor), cursor);
            DestroyCursor(blankCursor);
        }

        private void RestorePcCursor() { SystemParametersInfo(0x0057, 0, IntPtr.Zero, 0); } // SPI_SETCURSORS 恢复系统默认

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
                RestorePcCursor(); // 🌟 极其重要：关闭时必须解除鼠标封印！
                _isMouseOnTablet = false;
                _edgeHwnd = IntPtr.Zero;
                StatusText.Text = "状态: 🔮 键鼠穿越已关闭";
                Card_ZeroSync.Tag = "";
                if (btn != null) btn.Content = "🔮 开启边缘键鼠穿越 (无线 Flow)";
                return;
            }

            // 🌟 首次点击，启动结界
            // 🛡️ 第一层：绝对互斥锁，防止与虚拟副屏产生“争夺主权”和“双鼠标”死锁
            if (_activeDisplaySessions.Count > 0) {
                ShowCyberMessage("⚠️ 物理冲突阻断", "您正在使用【扩展电脑副屏】模式！\n\nWindows 在副屏模式下已原生接管了边缘滑动的逻辑。若此时强行开启穿越引擎，两个系统会疯狂争夺鼠标控制权，导致严重撕裂。\n\n👉 您直接将鼠标向右滑动即可跨屏，无需开启此结界！");
                return;
            }

            if (_selectedDevice == null) { ShowCyberMessage("⚠️ 未选择设备", "请先在右侧选择设备！"); return; }
            if (btn != null) btn.Content = "🛑 关闭边缘键鼠穿越";
            
            ZeroSyncOverlay.Visibility = Visibility.Collapsed;
            StatusText.Text = "状态: 🔮 正在召唤透明键鼠结界...";

            try {
                string targetId = _selectedDevice.Id;
                string args = "";

                // ⚡ 第三层：智能通道选择与音频剥离 (解决占用音频的 Bug)
                if (!_selectedDevice.IsWireless) {
                    // 方案 A (有线): 极致底层的纯物理 OTG 模拟，0视频 0音频，性能损耗为 0。
                    args = $"-s {targetId} --otg --window-title=SW_EdgeCross --window-borderless";
                } else {
                    // 方案 A (无线备用): 采用降维打击，强制挂载 --no-audio 彻底解决声音消失的恶性 Bug！
                    args = $"-s {targetId} --keyboard=uhid --mouse=uhid --no-audio --no-video --window-title=SW_EdgeCross --window-borderless";
                }

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = scrcpyPath,
                    Arguments = args,
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

                ShowCyberMessage("🔮 零延迟穿越结界 (Option A) 部署完毕", "底层 UHID / OTG 引擎已成功挂载！占用音频的 Bug 已被彻底剔除。\n\n👉 【进入结界】请将电脑鼠标一直向右滑，撞击屏幕最右侧，鼠标将从 PC 彻底隐身，并物理级注入移动端！\n👉 【返回电脑】由于采用了最原生的接管模式，系统会物理锁定光标。请按一下键盘上的【Alt 键】解除锁定，然后鼠标向左滑撞回边缘，即可瞬间解除隐身并回到 PC！\n\n*(注意：穿越中途若误按 Alt+Tab 切走焦点，也会自动强行召回)*");
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