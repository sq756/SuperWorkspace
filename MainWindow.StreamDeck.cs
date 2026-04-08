using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        private string _deckAuthToken = Guid.NewGuid().ToString("N"); // 🌟 一次性安全令牌防线
        private HttpListener? _streamDeckServer;
        private CancellationTokenSource? _streamDeckCts;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int KEYEVENTF_SCANCODE = 0x0008;

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // 🌟 新增：磁盘持久化配置引擎，所有的修改都会保存在电脑本地！
        private string GetOrCreateDeckConfig()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperWorkspace");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "deck_config.json");
            if (File.Exists(path)) return File.ReadAllText(path);

            // 初始自带的炫酷配置预设
            string defaultJson = @"[
                { ""id"": ""media"", ""name"": ""🎵 Media"", ""buttons"": [
                    { ""type"": ""dial"", ""icon"": ""🎛️"", ""text"": ""音量旋钮"", ""cmdUp"": ""vol_up"", ""cmdDown"": ""vol_down"", ""color"": ""widget-3x3"" },
                    { ""icon"": ""🔇"", ""text"": ""静音"", ""cmd"": ""vol_mute"", ""color"": ""btn red"" },
                    { ""icon"": ""⏯"", ""text"": ""播放/暂停"", ""cmd"": ""media_play"", ""color"": ""btn green"" },
                    { ""icon"": ""⏮"", ""text"": ""上一首"", ""cmd"": ""media_prev"" }, { ""icon"": ""⏭"", ""text"": ""下一首"", ""cmd"": ""media_next"" }
                ]},
                { ""id"": ""dev"", ""name"": ""👨‍💻 Dev"", ""buttons"": [
                    { ""icon"": ""📝"", ""text"": ""VS Code"", ""cmd"": ""dev_vscode"", ""color"": ""btn"" },
                    { ""icon"": ""⌨️"", ""text"": ""终端"", ""cmd"": ""dev_term"", ""color"": ""btn"" },
                    { ""icon"": ""📊"", ""text"": ""任务管理"", ""cmd"": ""dev_taskmgr"", ""color"": ""btn"" },
                    { ""icon"": ""🐳"", ""text"": ""Docker 面板"", ""cmd"": ""open_docker"", ""color"": ""btn green"" },
                    { ""icon"": ""🐧"", ""text"": ""Linux 工具"", ""cmd"": ""open_devtools"", ""color"": ""btn"" },
                    { ""icon"": """", ""text"": ""Git 同步"", ""cmd"": ""dev_git_sync"", ""color"": ""btn"" },
                    { ""icon"": ""🔒"", ""text"": ""锁定电脑"", ""cmd"": ""lock"", ""color"": ""btn red"" }
                ]},
                { ""id"": ""academic"", ""name"": ""🎓 Academic"", ""buttons"": [
                    { ""icon"": ""📚"", ""text"": ""一键引用"", ""cmd"": ""acad_cite"" },
                    { ""icon"": ""∑"", ""text"": ""插入矩阵公式"", ""cmd"": ""acad_matrix"" },
                    { ""icon"": ""✍️"", ""text"": ""手写转 LaTeX"", ""cmd"": ""acad_draw"" }
                ]},
                { ""id"": ""macro"", ""name"": ""🎮 Game"", ""buttons"": [
                    { ""icon"": ""🌟"", ""text"": ""原神一键日常"", ""cmd"": ""genshin_daily|C:\\Program Files\\Genshin Impact\\Genshin Impact Game\\YuanShen.exe"", ""color"": ""btn"", ""x"": 2, ""y"": 1 },
                    { ""icon"": ""⌨️"", ""text"": ""虚拟键盘"", ""cmd"": ""open_keyboard"", ""color"": ""btn green"", ""x"": 5, ""y"": 1 },
                    { ""icon"": ""🕹️"", ""text"": ""虚拟摇杆"", ""cmd"": ""open_joystick"", ""color"": ""btn red"", ""x"": 8, ""y"": 1 },
                    { ""type"": ""dial"", ""icon"": ""↕️"", ""text"": ""鼠标 Y 轴"", ""cmdUp"": ""mouse_y|-25"", ""cmdDown"": ""mouse_y|25"", ""color"": ""widget-3x3"", ""x"": 1, ""y"": 6 },
                    { ""type"": ""dial"", ""icon"": ""↔️"", ""text"": ""鼠标 X 轴"", ""cmdUp"": ""mouse_x|25"", ""cmdDown"": ""mouse_x|-25"", ""color"": ""widget-3x3"", ""x"": 12, ""y"": 6 }
                ]},
                { ""id"": ""apps"", ""name"": ""📱 Apps"", ""buttons"": [
                    { ""icon"": ""💬"", ""text"": ""一键微信"", ""cmd"": ""run|C:\Program Files\Tencent\Weixin\Weixin.exe"", ""color"": ""btn green"" },
                    { ""icon"": ""📺"", ""text"": ""哔哩哔哩"", ""cmd"": ""url|https://www.bilibili.com"", ""color"": ""btn red"" }
                ]},
                { ""id"": ""net"", ""name"": ""🌐 Network"", ""buttons"": [
                    { ""icon"": ""📡"", ""text"": ""查看 IP"", ""cmd"": ""net_ip"" },
                    { ""icon"": ""🔄"", ""text"": ""重置 DNS"", ""cmd"": ""net_flush"", ""color"": ""btn red"" },
                    { ""icon"": ""🔀"", ""text"": ""网络设置"", ""cmd"": ""net_proxy"", ""color"": ""btn green"" }
                ]},
                { ""id"": ""creative"", ""name"": ""🎨 Creative"", ""buttons"": [
                    { ""type"": ""dial"", ""icon"": ""🔍"", ""text"": ""缩放旋钮"", ""cmdUp"": ""cre_zoomin"", ""cmdDown"": ""cre_zoomout"", ""color"": ""widget-3x3"" },
                    { ""icon"": ""↩️"", ""text"": ""撤销"", ""cmd"": ""cre_undo"" }, { ""icon"": ""↪️"", ""text"": ""重做"", ""cmd"": ""cre_redo"" },
                    { ""icon"": ""🖌️"", ""text"": ""画笔 (B)"", ""cmd"": ""cre_brush"" }, { ""icon"": ""💾"", ""text"": ""保存"", ""cmd"": ""cre_save"", ""color"": ""btn green"" }
                ]}
            ]";
            File.WriteAllText(path, defaultJson);
            return defaultJson;
        }

        private void StartStreamDeckServer(int port)
        {
            _streamDeckCts = new CancellationTokenSource();
            var token = _streamDeckCts.Token;
            
            _streamDeckServer = new HttpListener();
            _streamDeckServer.Prefixes.Add($"http://localhost:{port}/");
            _streamDeckServer.Start();

            Task.Run(async () => 
            {
                while (!token.IsCancellationRequested && _streamDeckServer.IsListening)
                {
                    try
                    {
                        var context = await _streamDeckServer.GetContextAsync();
                        _ = Task.Run(async () => 
                        {
                            var req = context.Request;
                            var res = context.Response;

                            // 🌟 处理来自平板端的按键动作 (HTTP POST 兜底)
                            if (req.Url?.AbsolutePath == "/action" && req.HttpMethod == "POST")
                            {
                                string? cmd = req.QueryString["cmd"];
                                if (!string.IsNullOrEmpty(cmd))
                                {
                                    Dispatcher.Invoke(() => ExecuteMacro(cmd));
                                }
                                res.StatusCode = 200;
                                res.Close();
                                return;
                            }

                            // 🌟 新增：处理保存新按键配置的请求
                            if (req.Url?.AbsolutePath == "/save_config" && req.HttpMethod == "POST")
                            {
                                using var reader = new StreamReader(req.InputStream);
                                string json = await reader.ReadToEndAsync();
                                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperWorkspace");
                                File.WriteAllText(Path.Combine(dir, "deck_config.json"), json);
                                res.StatusCode = 200;
                                res.Close();
                                return;
                            }

                            // 🌟 架构升级：接入 WebSocket 全双工管道
                            if (req.Url?.AbsolutePath == "/ws" && req.IsWebSocketRequest)
                            {
                                // 🌟 核心防御：局域网 RCE 拦截器，校验动态 Token
                                if (req.QueryString["token"] != _deckAuthToken) {
                                    res.StatusCode = 403;
                                    res.Close();
                                    return;
                                }
                                
                                var wsContext = await context.AcceptWebSocketAsync(null);
                                var ws = wsContext.WebSocket;
                                _deckWebSocket = ws; // 🌟 绑定给 Sync 引擎用于推送剪贴板
                                SyncHistoryToTablet(); // 🌟 平板一连上，立刻补发它离线期间 PC 缓存的所有剪贴板！
                                
                                byte[] buffer = new byte[8192];
                                while (ws.State == System.Net.WebSockets.WebSocketState.Open && !token.IsCancellationRequested)
                                {
                                    // 🌟 大文件/超长剪贴板内存流接收器
                                    using var ms = new MemoryStream();
                                    WebSocketReceiveResult result;
                                    do {
                                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                                        ms.Write(buffer, 0, result.Count);
                                    } while (!result.EndOfMessage);

                                    if (result.MessageType == WebSocketMessageType.Text)
                                    {
                                        string msg = Encoding.UTF8.GetString(ms.ToArray());
                                        using JsonDocument doc = JsonDocument.Parse(msg);
                                        string wsCmd = doc.RootElement.GetProperty("cmd").GetString() ?? "";
                                        
                                        // 🌟 拦截并处理平板发回的剪贴板内容
                                        if (wsCmd.StartsWith("sync_clipboard|")) { UpdatePcClipboard(wsCmd.Substring(15)); continue; }

                                        Dispatcher.Invoke(() => ExecuteMacro(wsCmd));
                                    }
                                }
                                return;
                            }

                            // 🌟 渲染绚丽的平板端暗黑风 UI
                            // 🌟 核心引擎升级：数据驱动的 CSS Scroll Snap 动态多场景 UI
                            if (req.Url?.AbsolutePath == "/")
                            {
                                // 🌟 禁用缓存：防止平板浏览器加载旧界面的刺客！
                                res.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0");
                                string html = @"<html><head>
                                    <meta name='viewport' content='width=device-width, initial-scale=1.0, user-scalable=no, maximum-scale=1.0'>
                                    <meta name='mobile-web-app-capable' content='yes'>
                                    <style>
                                        body { background: #121212; color: white; font-family: 'Segoe UI', sans-serif; margin: 0; display: flex; flex-direction: column; height: 100vh; overflow: hidden; }
                                        /* 顶部导航栏 */
                                        .nav { display: flex; overflow-x: auto; background: #1e1e1e; padding: 15px 10px; scroll-snap-type: x mandatory; }
                                        .nav::-webkit-scrollbar { display: none; }
                                        .nav-item { padding: 8px 20px; scroll-snap-align: start; white-space: nowrap; border-radius: 20px; background: #333; margin-right: 10px; font-weight: bold; cursor: pointer; transition: 0.2s; }
                                        .nav-item.active { background: #007ACC; box-shadow: 0 0 10px rgba(0,122,204,0.5); }
                                        /* 🌟 积木网格系统 (Fractional Grid) */
                                        .container { flex: 1; display: flex; overflow-x: auto; scroll-snap-type: x mandatory; scroll-behavior: smooth; }
                                        .container::-webkit-scrollbar { display: none; }
                                        .page { min-width: 100vw; padding: 20px; box-sizing: border-box; scroll-snap-align: start; display: grid; grid-template-columns: repeat(12, 1fr); grid-auto-rows: 7vw; gap: 10px; align-content: start; overflow-y: auto; }
                                        /* 🌟 基础积木块定义 */
                                        .btn { grid-column: span 2; grid-row: span 2; background: #2D2D30; border-radius: 16px; padding: 10px; text-align: center; font-size: 14px; font-weight: bold; box-shadow: 0 4px 15px rgba(0,0,0,0.4); display: flex; flex-direction: column; align-items: center; justify-content: center; user-select: none; cursor: pointer; border: 1px solid #333;}
                                        .widget-1x1 { grid-column: span 1; grid-row: span 1; background: #2D2D30; border-radius: 10px; display: flex; flex-direction: column; align-items: center; justify-content: center; font-size: 10px; border: 1px solid #333; }
                                        .widget-3x3 { grid-column: span 3; grid-row: span 3; background: #2D2D30; border-radius: 24px; display: flex; flex-direction: column; align-items: center; justify-content: center; user-select: none; cursor: grab; border: 1px solid #333; touch-action: none; box-shadow: 0 4px 15px rgba(0,0,0,0.4); }
                                        .icon { font-size: 32px; margin-bottom: 8px; }
                                        .btn.red { background: #5a1a1a; border-color:#800; }
                                        .btn.green { background: #1a4a2a; border-color:#060; }
                                        
                                        /* 🌟 响应式降级：竖屏下自动降维为 6 列流式布局，屏蔽绝对坐标！ */
                                        @media (orientation: portrait) {
                                            .page { grid-template-columns: repeat(6, 1fr); grid-auto-rows: 14vw; }
                                            .abs-pos { grid-column: span var(--w) !important; grid-row: span var(--h) !important; }
                                        }
                                        @media (orientation: landscape) {
                                            .abs-pos { grid-column: var(--x) / span var(--w) !important; grid-row: var(--y) / span var(--h) !important; }
                                        }
                                        /* 🎮 全屏外设覆盖层样式 */
                                        .overlay-container { display: none; position: fixed; top: 0; left: 0; width: 100vw; height: 100vh; background: rgba(0,0,0,0.9); z-index: 1000; padding: 30px; box-sizing: border-box; backdrop-filter: blur(10px); touch-action: none; }
                                        .close-btn { position: absolute; top: 20px; left: 50%; transform: translateX(-50%); font-size: 16px; padding: 10px 30px; background: #d32f2f; border-radius: 20px; cursor: pointer; font-weight: bold; z-index: 1001; border: 1px solid #ff5252;}
                                        .joystick-zone { width: 180px; height: 180px; background: rgba(255,255,255,0.05); border-radius: 50%; position: relative; border: 3px solid #444; box-shadow: inset 0 0 20px rgba(0,0,0,0.5); touch-action: none; }
                                        .joystick-knob { width: 70px; height: 70px; background: radial-gradient(circle, #555, #222); border-radius: 50%; position: absolute; top: 55px; left: 55px; pointer-events: none; box-shadow: 0 10px 20px rgba(0,0,0,0.5); border: 2px solid #666; transition: transform 0.05s linear; }
                                        .action-btn { position: absolute; width: 60px; height: 60px; border-radius: 50%; background: #333; color: white; display: flex; justify-content: center; align-items: center; font-size: 22px; font-weight: bold; border: 2px solid #555; box-shadow: 0 5px 15px rgba(0,0,0,0.5); user-select: none; cursor: pointer; touch-action: none; transition: 0.1s;}
                                        .action-btn:active { background: #A074E5; border-color: #A074E5; transform: scale(0.9); }
                                        /* 动态全键盘样式 */
                                        .kb-row { display: flex; justify-content: center; gap: 4px; margin-bottom: 6px; width: 100%; max-width: 1200px; }
                                        .kb-key { background: #2A2A2D; border: 1px solid #444; border-radius: 6px; display: flex; align-items: center; justify-content: center; font-weight: bold; font-size: 14px; cursor: pointer; flex: 1; height: 40px; touch-action: none; transition: background 0.1s; color: white; box-shadow: 0 2px 5px rgba(0,0,0,0.3);}
                                        .kb-key.active { background: #007ACC; border-color: #007ACC; transform: scale(0.92); }
                                        .kb-key.dark { background: #1E1E1E; color: #AAA; }
                                        /* 🌟 悬浮添加按钮与配置窗样式 */
                                        .fab { position: fixed; right: 25px; bottom: 25px; width: 60px; height: 60px; background: #007ACC; border-radius: 50%; display: flex; justify-content: center; align-items: center; font-size: 36px; font-weight: bold; box-shadow: 0 4px 15px rgba(0,0,0,0.6); cursor: pointer; z-index: 1000; user-select: none; }
                                        .fab:active { transform: scale(0.9); }
                                        .modal-input { background: #333; color: white; border: 1px solid #555; border-radius: 6px; padding: 10px; margin-bottom: 15px; width: 100%; box-sizing: border-box; font-size: 14px; outline: none;}
                                        /* 🌟 剪贴板同步浮窗样式 */
                                        .toast { display: none; position: fixed; top: 20px; left: 50%; transform: translateX(-50%); background: #00CA72; color: white; padding: 15px 25px; border-radius: 30px; font-weight: bold; box-shadow: 0 5px 15px rgba(0,202,114,0.4); z-index: 10005; cursor: pointer; font-size: 16px; transition: 0.2s;}
                                        .toast:active { transform: translateX(-50%) scale(0.95); }
                                        /* 🌟 旋钮微件内部样式 */
                                        .dial-ring { width: 80%; padding-bottom: 80%; border-radius: 50%; border: 4px solid #444; position: relative; margin-bottom: 10px; box-sizing: border-box; background: #1a1a1a; box-shadow: inset 0 5px 15px rgba(0,0,0,0.8); }
                                        .dial-knob { position: absolute; top: 15%; left: 15%; width: 70%; height: 70%; border-radius: 50%; background: linear-gradient(145deg, #555, #222); box-shadow: 0 5px 15px rgba(0,0,0,0.6); transition: transform 0.05s linear; }
                                        .dial-indicator { position: absolute; top: 8px; left: 50%; transform: translateX(-50%); width: 6px; height: 18px; background: #A074E5; border-radius: 3px; box-shadow: 0 0 10px #A074E5; }
                                        /* 🌟 编辑模式：抖动动画与虚线框 */
                                        @keyframes jiggle { 0% { transform: rotate(-1deg) scale(0.98); } 50% { transform: rotate(1deg) scale(1.02); } 100% { transform: rotate(-1deg) scale(0.98); } }
                                        .edit-mode .btn, .edit-mode .widget-3x3, .edit-mode .widget-1x1 { animation: jiggle 0.35s infinite; border: 2px dashed #A074E5 !important; cursor: grab !important; z-index: 100; }
                                        .edit-mode .dial-knob { pointer-events: none; }
                                        .edit-mode .btn:active, .edit-mode .widget-3x3:active, .edit-mode .widget-1x1:active { cursor: grabbing !important; }
                                    </style>
                                    <script>
                                        // 🌟 1. 配置化 JSON 数据模型 (从 C# 动态注入加载)
                                        let deckConfig = DECK_CONFIG_PAYLOAD;

                                        // 🌟 2. 建立极速 WebSocket 管道
                                        var ws = new WebSocket('ws://' + location.host + '/ws?token=AUTH_TOKEN_PAYLOAD');
                                        
                                        // 🌟 接收电脑传来的剪贴板
                                        let pendingClipText = '';
                                        ws.onmessage = function(event) {
                                            let data = JSON.parse(event.data);
                                            if (data.type === 'CLIPBOARD_SYNC') {
                                                pendingClipText = data.content;
                                                let toast = document.getElementById('clip-toast');
                                                toast.innerText = '📋 收到 PC 剪贴板，点我粘贴！';
                                                toast.style.display = 'block';
                                                setTimeout(() => toast.style.display = 'none', 8000); // 8秒后自动消失
                                            }
                                            if (data.type === 'CLIPBOARD_HISTORY') {
                                                let list = document.getElementById('clip-history-list');
                                                list.innerHTML = '';
                                                data.content.forEach(text => {
                                                    let card = document.createElement('div');
                                                    card.style.cssText = 'background: #333; padding: 15px; border-radius: 8px; color: white; word-break: break-all; cursor: pointer; border: 1px solid #555; transition: 0.1s;';
                                                    card.innerText = text.length > 100 ? text.substring(0, 100) + '...' : text;
                                                    card.onpointerdown = () => { card.style.transform = 'scale(0.95)'; card.style.background = '#007ACC'; };
                                                    card.onpointerup = () => {
                                                        card.style.transform = 'none'; card.style.background = '#333';
                                                        pendingClipText = text; copyToTablet(); closeOverlay();
                                                    };
                                                    list.appendChild(card);
                                                });
                                            }
                                        };

                                        function send(e, cmd) { 
                                            e.preventDefault(); 
                                            document.documentElement.requestFullscreen().catch(err => {}); 
                                            if (cmd === 'open_joystick') { openOverlay('joystick'); return; }
                                            if (cmd === 'open_keyboard') { openOverlay('keyboard'); return; }
                                            if (cmd === 'open_docker') { openOverlay('docker'); return; }
                                            if (cmd === 'open_devtools') { openOverlay('devtools'); return; }
                                            if (cmd === 'open_clipboard') { openOverlay('clipboard'); return; }
                                            // 🌟 拦截：把平板剪贴板发回给电脑
                                            if (cmd === 'push_clipboard') {
                                                if (navigator.clipboard && window.isSecureContext) {
                                                    navigator.clipboard.readText().then(text => {
                                                        if(ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ cmd: 'sync_clipboard|' + text }));
                                                        alert('✅ 剪贴板已推送到电脑！');
                                                    }).catch(err => alert('无法读取剪贴板，请检查浏览器权限设置！'));
                                                } else {
                                                    let text = prompt('【局域网安全限制】浏览器限制了自动读取。\n请长按下方输入框【粘贴】您的内容，点击确定发送给电脑：');
                                                    if (text) { if(ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ cmd: 'sync_clipboard|' + text })); alert('✅ 已推送到电脑！'); }
                                                }
                                                return;
                                            }
                                            if(ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ cmd: cmd }));
                                        }
                                        window.oncontextmenu = function(e) { e.preventDefault(); e.stopPropagation(); return false; };

                                        // 🌟 交互革命：全局积木抖动编辑模式
                                        let editingTab = -1; let editingBtn = -1;
                                        let isGlobalEditMode = false;
                                        let fabTimer; let fabLongPress = false;

                                        function handleFabDown(e) {
                                            e.preventDefault(); fabLongPress = false;
                                            fabTimer = setTimeout(() => { fabLongPress = true; toggleEditMode(); if(navigator.vibrate) navigator.vibrate(50); }, 500);
                                        }
                                        function handleFabUp(e) {
                                            e.preventDefault(); clearTimeout(fabTimer);
                                            if (!fabLongPress) { if (isGlobalEditMode) toggleEditMode(); else openAddModal(); }
                                        }

                                        function toggleEditMode() {
                                            isGlobalEditMode = !isGlobalEditMode;
                                            let fab = document.getElementById('fab');
                                            if (isGlobalEditMode) {
                                                document.body.classList.add('edit-mode'); fab.innerHTML = '✅'; fab.style.background = '#00CA72';
                                            } else {
                                                document.body.classList.remove('edit-mode'); fab.innerHTML = '+'; fab.style.background = '#007ACC';
                                            }
                                        }

                                        function openEditModal(tIdx, bIdx) {
                                            editingTab = tIdx; editingBtn = bIdx;
                                            let b = deckConfig[tIdx].buttons[bIdx];
                                            document.getElementById('modal-title').innerText = '✏️ 编辑积木属性';
                                            document.getElementById('add-tab').value = tIdx;
                                            document.getElementById('add-tab').disabled = true;
                                            document.getElementById('add-icon').value = b.icon;
                                            document.getElementById('add-name').value = b.text;
                                            
                                            // 解析当前的尺寸和颜色
                                            let cls = b.color || 'btn';
                                            let size = 'btn'; let col = '';
                                            if (cls.includes('widget-1x1')) size = 'widget-1x1';
                                            else if (cls.includes('widget-3x3')) size = 'widget-3x3';
                                            if (cls.includes('green')) col = ' green'; else if (cls.includes('red')) col = ' red';
                                            document.getElementById('add-size').value = size;
                                            document.getElementById('add-color').value = col;
                                            
                                            let type = 'cmd|'; let val = b.cmd;
                                            if(b.cmd.includes('|')) { let p = b.cmd.split('|'); type = p[0] + '|'; val = p.slice(1).join('|'); }
                                            
                                            let typeSel = document.getElementById('add-type');
                                            if(![...typeSel.options].find(o=>o.value===type)) typeSel.innerHTML += `<option value='${type}'>⚙️ 内置系统指令</option>`;
                                            typeSel.value = type; document.getElementById('add-val').value = val;
                                            
                                            // 🌟 开启积木重排模式
                                            document.getElementById('edit-btn-box').style.display = 'flex';
                                            document.getElementById('add-modal').style.display = 'flex';
                                        }

                                        function openAddModal() {
                                            editingTab = -1; editingBtn = -1;
                                            document.getElementById('modal-title').innerText = '➕ 添加新积木';
                                            document.getElementById('add-tab').disabled = false;
                                            document.getElementById('add-icon').value = '✨';
                                            document.getElementById('add-name').value = '';
                                            document.getElementById('add-type').value = 'run|';
                                            document.getElementById('add-size').value = 'btn';
                                            document.getElementById('add-color').value = '';
                                            document.getElementById('add-val').value = '';
                                            document.getElementById('edit-btn-box').style.display = 'none';
                                            document.getElementById('add-modal').style.display = 'flex';
                                        }

                                        function saveBtn() {
                                            let tIdx = editingTab !== -1 ? editingTab : parseInt(document.getElementById('add-tab').value);
                                            let icon = document.getElementById('add-icon').value || '✨';
                                            let name = document.getElementById('add-name').value || '新按键';
                                            let type = document.getElementById('add-type').value;
                                            let val = document.getElementById('add-val').value;
                                            let finalColor = document.getElementById('add-size').value + document.getElementById('add-color').value;
                                            if (!val) return alert('请输入具体的执行内容或路径！');
                                            let finalCmd = type === 'cmd|' ? val : type + val;
                                            if (editingBtn !== -1) deckConfig[tIdx].buttons[editingBtn] = { icon: icon, text: name, cmd: finalCmd, color: finalColor, type: (finalColor.includes('3x3') ? 'dial' : 'button') };
                                            else deckConfig[tIdx].buttons.push({ icon: icon, text: name, cmd: finalCmd, color: finalColor, type: (finalColor.includes('3x3') ? 'dial' : 'button') });
                                            fetch('/save_config', { method: 'POST', body: JSON.stringify(deckConfig) }).then(() => location.reload());
                                        }

                                        function deleteBtn() {
                                            if(editingBtn === -1) return;
                                            if(confirm('确定要删除这个按键吗？')) {
                                                deckConfig[editingTab].buttons.splice(editingBtn, 1);
                                                fetch('/save_config', { method: 'POST', body: JSON.stringify(deckConfig) }).then(() => location.reload());
                                            }
                                        }

                                        // 🌟 积木重排：前移与后移
                                        function moveBtn(dir) {
                                            if(editingBtn === -1) return;
                                            let arr = deckConfig[editingTab].buttons;
                                            let newIdx = editingBtn + dir;
                                            if (newIdx >= 0 && newIdx < arr.length) {
                                                let temp = arr[editingBtn];
                                                arr[editingBtn] = arr[newIdx];
                                                arr[newIdx] = temp;
                                                fetch('/save_config', { method: 'POST', body: JSON.stringify(deckConfig) }).then(() => location.reload());
                                            }
                                        }

                                        // 🌟 写入平板剪贴板的核心突破口 (支持 HTTP 降级写入)
                                        function copyToTablet() {
                                            let successCb = () => {
                                                let toast = document.getElementById('clip-toast');
                                                toast.innerText = '✅ 已成功复制到平板！';
                                                setTimeout(() => { toast.style.display = 'none'; }, 1500);
                                            };
                                            if (navigator.clipboard && window.isSecureContext) {
                                                navigator.clipboard.writeText(pendingClipText).then(successCb).catch(err => alert('复制失败，请重试！'));
                                            } else {
                                                let ta = document.createElement('textarea'); ta.value = pendingClipText;
                                                ta.style.position = 'fixed'; ta.style.top = '-9999px'; document.body.appendChild(ta);
                                                ta.focus(); ta.select();
                                                try { document.execCommand('copy'); successCb(); } catch (err) { alert('写入失败，您的浏览器拒绝了 HTTP 剪贴板操作！'); }
                                                document.body.removeChild(ta);
                                            }
                                        }

                                        // 🌟 游戏外设逻辑 (长按控制与摇杆计算)
                                        function openOverlay(type) {
                                            document.getElementById('overlay-container').style.display = 'block';
                                            document.getElementById('joystick-ui').style.display = (type === 'joystick') ? 'flex' : 'none';
                                            document.getElementById('keyboard-ui').style.display = (type === 'keyboard') ? 'flex' : 'none';
                                            document.getElementById('docker-ui').style.display = (type === 'docker') ? 'flex' : 'none';
                                            document.getElementById('devtools-ui').style.display = (type === 'devtools') ? 'flex' : 'none';
                                            document.getElementById('clipboard-ui').style.display = (type === 'clipboard') ? 'flex' : 'none';
                                        }
                                        function closeOverlay() {
                                            document.getElementById('overlay-container').style.display = 'none';
                                            updateJoyKeys(0,0); // 重置状态
                                        }
                                        function sendKey(cmd, isDown) {
                                            if(ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ cmd: cmd + (isDown ? '_down' : '_up') }));
                                            
                                            // 🌟 杀手级特性 3：硬件级物理震动反馈 (支持的安卓平板会产生清脆的按键反馈)
                                            if (isDown && navigator.vibrate) navigator.vibrate(15); 
                                        }

                                        let joyZone, joyKnob;
                                        let joyActive = false, joyCenter = {x:0, y:0};
                                        let curJoyKeys = { w: false, a: false, s: false, d: false };

                                        function updateJoy(e) {
                                            let dx = e.clientX - joyCenter.x, dy = e.clientY - joyCenter.y;
                                            let distance = Math.min(55, Math.sqrt(dx*dx + dy*dy));
                                            let angle = Math.atan2(dy, dx);
                                            let nx = distance * Math.cos(angle), ny = distance * Math.sin(angle);
                                            joyKnob.style.transform = `translate(${nx}px, ${ny}px)`;
                                            
                                            // 🌟 更精细的摇杆判定阈值防抖
                                            let th = 20; 
                                            let newKeys = { w: ny < -th, s: ny > th, a: nx < -th, d: nx > th };
                                            for(let k in newKeys) {
                                                if (newKeys[k] !== curJoyKeys[k]) {
                                                    curJoyKeys[k] = newKeys[k];
                                                    sendKey('key_' + k, newKeys[k] ? 1 : 0);
                                                }
                                            }
                                        }

                                        // 🌟 动态生成 104 键满血版电竞全键盘
                                        function buildKeyboard() {
                                            let kb = document.getElementById('keyboard-ui');
                                            kb.innerHTML = '';
                                            const layouts = [
                                                [{k:'Esc',v:'esc', c:'dark'}, {k:'F1',v:'f1', c:'dark'}, {k:'F2',v:'f2', c:'dark'}, {k:'F3',v:'f3', c:'dark'}, {k:'F4',v:'f4', c:'dark'}, {k:'F5',v:'f5', c:'dark'}, {k:'F6',v:'f6', c:'dark'}, {k:'F7',v:'f7', c:'dark'}, {k:'F8',v:'f8', c:'dark'}, {k:'F9',v:'f9', c:'dark'}, {k:'F10',v:'f10', c:'dark'}, {k:'F11',v:'f11', c:'dark'}, {k:'F12',v:'f12', c:'dark'}],
                                                [{k:'~',v:'~', c:'dark'}, {k:'1',v:'1'}, {k:'2',v:'2'}, {k:'3',v:'3'}, {k:'4',v:'4'}, {k:'5',v:'5'}, {k:'6',v:'6'}, {k:'7',v:'7'}, {k:'8',v:'8'}, {k:'9',v:'9'}, {k:'0',v:'0'}, {k:'-',v:'-'}, {k:'=',v:'='}, {k:'Back', v:'backspace', w:2, c:'dark'}],
                                                [{k:'Tab', v:'tab', w:1.5, c:'dark'}, {k:'Q',v:'Q'}, {k:'W',v:'W'}, {k:'E',v:'E'}, {k:'R',v:'R'}, {k:'T',v:'T'}, {k:'Y',v:'Y'}, {k:'U',v:'U'}, {k:'I',v:'I'}, {k:'O',v:'O'}, {k:'P',v:'P'}, {k:'[',v:'['}, {k:']',v:']'}, {k:'\\\\',v:'\\\\'}],
                                                [{k:'Caps', v:'caps', w:2, c:'dark'}, {k:'A',v:'A'}, {k:'S',v:'S'}, {k:'D',v:'D'}, {k:'F',v:'F'}, {k:'G',v:'G'}, {k:'H',v:'H'}, {k:'J',v:'J'}, {k:'K',v:'K'}, {k:'L',v:'L'}, {k:';',v:';'}, {k:""'"",v:""'""}, {k:'Enter', v:'enter', w:2.5, c:'dark'}],
                                                [{k:'Shift', v:'shift', w:2.5, c:'dark'}, {k:'Z',v:'Z'}, {k:'X',v:'X'}, {k:'C',v:'C'}, {k:'V',v:'V'}, {k:'B',v:'B'}, {k:'N',v:'N'}, {k:'M',v:'M'}, {k:',',v:','}, {k:'.',v:'.'}, {k:'/',v:'/'}, {k:'Shift', v:'shift', w:2.5, c:'dark'}],
                                                [{k:'Ctrl', v:'ctrl', w:1.5, c:'dark'}, {k:'Win', v:'win', w:1.5, c:'dark'}, {k:'Alt', v:'alt', w:1.5, c:'dark'}, {k:'Space', v:'space', w:6}, {k:'Alt', v:'alt', w:1.5, c:'dark'}, {k:'Win', v:'win', w:1.5, c:'dark'}, {k:'Ctrl', v:'ctrl', w:1.5, c:'dark'}, {k:'←', v:'left', c:'dark'}, {k:'↓', v:'down', c:'dark'}, {k:'↑', v:'up', c:'dark'}, {k:'→', v:'right', c:'dark'}]
                                            ];
                                            layouts.forEach(row => {
                                                let r = document.createElement('div'); r.className = 'kb-row';
                                                row.forEach(key => {
                                                    let kBtn = document.createElement('div'); kBtn.className = 'kb-key';
                                                    if(key.c) kBtn.classList.add(key.c);
                                                    if(key.w) kBtn.style.flex = key.w;
                                                    kBtn.innerText = key.k;
                                                    kBtn.onpointerdown = (e) => { e.target.classList.add('active'); sendKey('key_' + key.v, 1); };
                                                    kBtn.onpointerup = (e) => { e.target.classList.remove('active'); sendKey('key_' + key.v, 0); };
                                                    kBtn.onpointercancel = (e) => { e.target.classList.remove('active'); sendKey('key_' + key.v, 0); };
                                                    r.appendChild(kBtn);
                                                }); kb.appendChild(r);
                                            });
                                        }

                                        // 🌟 3. 动态渲染页面
                                        window.onload = () => {
                                            const nav = document.getElementById('nav');
                                            const container = document.getElementById('container');
                                            
                                            buildKeyboard(); // 构建全键盘

                                            joyZone = document.getElementById('joystick-zone');
                                            joyKnob = document.getElementById('joystick-knob');
                                            joyZone.addEventListener('pointerdown', e => {
                                                joyActive = true; let rect = joyZone.getBoundingClientRect();
                                                joyCenter = { x: rect.left + rect.width/2, y: rect.top + rect.height/2 };
                                                updateJoy(e);
                                            });
                                            window.addEventListener('pointermove', e => { if(joyActive) updateJoy(e); });
                                            window.addEventListener('pointerup', e => { if(joyActive) { joyActive = false; joyKnob.style.transform = 'translate(0, 0)'; updateJoyKeys(0,0); }});
                                            window.addEventListener('pointercancel', e => { if(joyActive) { joyActive = false; joyKnob.style.transform = 'translate(0, 0)'; updateJoyKeys(0,0); }});

                                            deckConfig.forEach((tab, index) => {
                                                // 渲染顶部 Tab
                                                let navItem = document.createElement('div');
                                                navItem.className = 'nav-item' + (index === 0 ? ' active' : '');
                                                navItem.innerText = tab.name;
                                                navItem.onclick = () => { document.getElementById('page_'+tab.id).scrollIntoView(); };
                                                nav.appendChild(navItem);
                                                
                                                // 渲染按键页面
                                                let page = document.createElement('div');
                                                page.className = 'page'; page.id = 'page_' + tab.id;
                                                tab.buttons.forEach((b, btnIdx) => {
                                                    let btn = document.createElement('div');
                                                    
                                                    // 🌟 沙盒坐标引擎：如果有独立坐标，允许它放置在网格的任何空白处！
                                                    let spanW = 2, spanH = 2;
                                                    if (b.color && b.color.includes('1x1')) { spanW = 1; spanH = 1; }
                                                    if (b.color && b.color.includes('3x3')) { spanW = 3; spanH = 3; }
                                                    btn.style.setProperty('--w', spanW);
                                                    btn.style.setProperty('--h', spanH);
                                                    if (b.x && b.y) {
                                                        btn.style.setProperty('--x', b.x);
                                                        btn.style.setProperty('--y', b.y);
                                                        btn.classList.add('abs-pos'); // 注入响应式坐标类
                                                    }
                                                    
                                                    let isDraggingBtn = false; let dragStartX = 0; let dragStartY = 0;

                                                    // 🌟 活体仪表盘：渲染巨型物理旋钮 (span 4x4)
                                                    if (b.type === 'dial') {
                                                        btn.className = b.color || 'widget-4x4';
                                                        btn.innerHTML = `<div class='dial-ring' id='ring_${tab.id}_${btnIdx}'><div class='dial-knob' id='knob_${tab.id}_${btnIdx}'><div class='dial-indicator'></div></div><div style='position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);font-size:36px;pointer-events:none'>${b.icon}</div></div><div style='font-size:15px; font-weight:bold; color:#CCC'>${b.text}</div>`;
                                                        
                                                        let currentAngle = 0; let active = false;
                                                        let centerX = 0; let centerY = 0; let lastAngle = 0;
                                                        let accumulatedDelta = 0; 
                                                        
                                                        btn.onpointerdown = (e) => { 
                                                            if (isGlobalEditMode) { 
                                                                openEditModal(index, btnIdx); 
                                                                isDraggingBtn = true; dragStartX = e.clientX; dragStartY = e.clientY;
                                                                btn.style.zIndex = 2000; btn.style.transition = 'none';
                                                                btn.setPointerCapture(e.pointerId); e.stopPropagation();
                                                                return; 
                                                            }
                                                            active = true; btn.style.cursor = 'grabbing'; btn.setPointerCapture(e.pointerId); 
                                                            
                                                            // 精准获取内侧“圆环”的物理中心
                                                            let rect = document.getElementById(`ring_${tab.id}_${btnIdx}`).getBoundingClientRect();
                                                            centerX = rect.left + rect.width / 2;
                                                            centerY = rect.top + rect.height / 2;
                                                            lastAngle = Math.atan2(e.clientY - centerY, e.clientX - centerX) * 180 / Math.PI;
                                                            e.stopPropagation(); 
                                                        };
                                                        btn.onpointermove = (e) => {
                                                            if (isGlobalEditMode) {
                                                                if (!isDraggingBtn) return;
                                                                let dx = e.clientX - dragStartX; let dy = e.clientY - dragStartY;
                                                                btn.style.transform = `translate(${dx}px, ${dy}px) scale(1.05)`;
                                                                e.stopPropagation(); return;
                                                            }
                                                            if (!active) return;
                                                            let newAngle = Math.atan2(e.clientY - centerY, e.clientX - centerX) * 180 / Math.PI;
                                                            let deltaAngle = newAngle - lastAngle;
                                                            if (deltaAngle > 180) deltaAngle -= 360;
                                                            if (deltaAngle < -180) deltaAngle += 360;
                                                            
                                                            // 🌟 视控分离引擎：画面 100% 跟手旋转，指令按度数累加发送！
                                                            if (Math.abs(deltaAngle) > 0) {
                                                                currentAngle += deltaAngle;
                                                                document.getElementById(`knob_${tab.id}_${btnIdx}`).style.transform = `rotate(${currentAngle}deg)`;
                                                                
                                                                accumulatedDelta += deltaAngle;
                                                                if (Math.abs(accumulatedDelta) > 15) { 
                                                                    if(ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ cmd: accumulatedDelta > 0 ? b.cmdUp : b.cmdDown }));
                                                                    if (navigator.vibrate) navigator.vibrate(5); 
                                                                    accumulatedDelta = 0; 
                                                                }
                                                                lastAngle = newAngle;
                                                            }
                                                            e.stopPropagation();
                                                        };
                                                        btn.onpointerup = (e) => { if (isGlobalEditMode) return; active = false; btn.style.cursor = 'grab'; btn.releasePointerCapture(e.pointerId); };
                                                        btn.onpointercancel = (e) => { if (isGlobalEditMode) return; active = false; btn.style.cursor = 'grab'; btn.releasePointerCapture(e.pointerId); };
                                                    } 
                                                    // 🌟 渲染普通按钮
                                                    else {
                                                        btn.className = b.color || 'btn';
                                                        btn.innerHTML = `<div class='icon'>${b.icon}</div>${b.text}`;
                                                        btn.onpointerdown = (e) => {
                                                            if (isGlobalEditMode) { openEditModal(index, btnIdx); return; }
                                                            btn.style.transform = 'scale(0.92)'; btn.style.background = '#007ACC';
                                                        };
                                                        btn.onpointerup = (e) => {
                                                            if (isGlobalEditMode) return;
                                                            btn.style.transform = 'none'; btn.style.background = '';
                                                            send(e, b.cmd);
                                                        };
                                                        btn.onpointercancel = (e) => { if (isGlobalEditMode) return; btn.style.transform = 'none'; btn.style.background = ''; };
                                                    }
                                                    page.appendChild(btn);
                                                });
                                                container.appendChild(page);
                                            });
                                            
                                            // 监听滑动联动 Tab 高亮
                                            container.addEventListener('scroll', () => {
                                                let idx = Math.round(container.scrollLeft / window.innerWidth);
                                                document.querySelectorAll('.nav-item').forEach((el, i) => {
                                                    el.classList.toggle('active', i === idx);
                                                });
                                            });
                                        };
                                    </script>
                                    </head>
                                    <body>
                                        <div class='nav' id='nav'></div>
                                        <div class='container' id='container'></div>
                                        
                                        <!-- 🌟 顶部通知浮窗 -->
                                        <div id='clip-toast' class='toast' onpointerdown='copyToTablet()'>📋 收到 PC 剪贴板，点我粘贴！</div>

                                        <!-- 🌟 右下角悬浮添加按钮 -->
                                        <div id='fab' class='fab' onpointerdown='handleFabDown(event)' onpointerup='handleFabUp(event)' onpointercancel='handleFabUp(event)'>+</div>

                                        <!-- 🌟 动态添加/编辑弹窗面板 -->
                                        <div id='add-modal' style='display:none; position:fixed; top:20px; right:20px; width:480px; background:rgba(37,37,38,0.95); z-index:9999; border-radius:12px; box-shadow: 0 10px 30px rgba(0,0,0,0.8); border: 1px solid #555; backdrop-filter: blur(10px); padding:20px 25px; max-height: 90vh; overflow-y: auto;'>
                                            <h3 id='modal-title' style='margin-top:0; margin-bottom:15px; color:#A074E5; text-align:center;'>➕ 添加新积木</h3>
                                            
                                            <div style='display:flex; gap:15px; margin-bottom:12px;'>
                                                <div style='flex:1;'><label style='font-size:12px;color:#aaa;'>目标分类:</label><select id='add-tab' class='modal-input'></select></div>
                                                <div style='flex:1;'><label style='font-size:12px;color:#aaa;'>动作类型:</label>
                                                    <select id='add-type' class='modal-input'>
                                                        <option value='url|'>🌐 打开网址</option>
                                                        <option value='run|'>💻 运行软件</option>
                                                        <option value='hotkey|'>⌨️ 发送快捷键</option>
                                                        <option value='genshin_daily|'>🌟 原神清日常</option>
                                                        <option value='cmd|open_clipboard'>📜 打开剪贴板</option>
                                                        <option value='cmd|push_clipboard'>📤 推送剪贴板</option>
                                                    </select>
                                                </div>
                                            </div>

                                            <div style='display:flex; gap:15px; margin-bottom:12px;'>
                                                <div style='flex:1;'><label style='font-size:12px;color:#aaa;'>积木尺寸:</label>
                                                    <select id='add-size' class='modal-input'>
                                                        <option value='widget-1x1'>迷你 (1格)</option>
                                                        <option value='btn'>标准 (2x2格)</option>
                                                        <option value='widget-3x3'>大微件/旋钮 (3x3)</option>
                                                    </select>
                                                </div>
                                                <div style='flex:1;'><label style='font-size:12px;color:#aaa;'>底色主题:</label>
                                                    <select id='add-color' class='modal-input'>
                                                        <option value=''>深灰 (默认)</option>
                                                        <option value=' green'>墨绿</option>
                                                        <option value=' red'>暗红</option>
                                                    </select>
                                                </div>
                                            </div>

                                            <div style='display:flex; gap:15px; margin-bottom:12px;'>
                                                <div style='flex:1; max-width:80px;'><label style='font-size:12px;color:#aaa;'>图标:</label><input id='add-icon' value='✨' class='modal-input' style='text-align:center;'></div>
                                                <div style='flex:1;'><label style='font-size:12px;color:#aaa;'>按键名称:</label><input id='add-name' placeholder='例如: 启动邮箱' class='modal-input'></div>
                                            </div>

                                            <div style='margin-bottom:15px;'>
                                                <label style='font-size:12px;color:#aaa;'>具体内容/文件绝对路径:</label>
                                                <input id='add-val' placeholder='例如: D:\games\YuanShen.exe' class='modal-input'>
                                            </div>
                                            
                                            <button id='edit-btn-box' onpointerdown='deleteBtn()' style='display:none; width:100%; padding:10px; background:#d32f2f; color:white; border:none; border-radius:8px; font-weight:bold; margin-bottom:12px; cursor:pointer;'>🗑️ 删除此按键</button>
                                            
                                            <div style='display:flex; gap:15px;'>
                                                <button onpointerdown='document.getElementById(""add-modal"").style.display=""none""' style='flex:1; padding:10px; background:#444; color:white; border:none; border-radius:8px; font-weight:bold; cursor:pointer;'>取消</button>
                                                <button onpointerdown='saveBtn()' style='flex:1; padding:10px; background:#A074E5; color:white; border:none; border-radius:8px; font-weight:bold; cursor:pointer;'>💾 保存</button>
                                            </div>
                                            </div>
                                            
                                            <!-- 🌟 Linux/DevTools 次级控制面板 -->
                                            <div id='devtools-ui' style='width:100%; height:100%; flex-direction:column; align-items:center; justify-content:center; gap: 20px; display: none; overflow-y: auto; padding-bottom: 30px;'>
                                                <h2 style='margin-bottom: 10px; color: white;'>🐧 Linux 神器快捷键</h2>
                                                
                                                <h3 style='color: #00CA72; margin: 5px 0;'>Vim 编辑器</h3>
                                                <div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 15px; width: 100%; max-width: 500px; margin-bottom: 15px;'>
                                                    <div class='btn' onpointerdown='send(event, ""vim_wq"")'><div class='icon'>💾</div>保存并退出</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_q_bang"")'><div class='icon'>🚪</div>强制退出</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_dd"")'><div class='icon'>✂️</div>删除当前行</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_u"")'><div class='icon'>↩️</div>撤销 (u)</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_search"")'><div class='icon'>🔍</div>搜索 (/)</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_insert"")'><div class='icon'>✍️</div>插入模式 (i)</div>
                                                </div>

                                                <h3 style='color: #00CA72; margin: 5px 0;'>Tmux 终端复用</h3>
                                                <div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 15px; width: 100%; max-width: 500px;'>
                                                    <div class='btn green' onpointerdown='send(event, ""tmux_new"")'><div class='icon'>🪟</div>新建窗口 (c)</div>
                                                    <div class='btn green' onpointerdown='send(event, ""tmux_vsplit"")'><div class='icon'>⏸️</div>垂直分割 (%)</div>
                                                    <div class='btn green' onpointerdown='send(event, ""tmux_hsplit"")'><div class='icon'>=</div>水平分割 ("")</div>
                                                    <div class='btn' onpointerdown='send(event, ""tmux_next"")'><div class='icon'>⏭️</div>下一个窗口 (n)</div>
                                                    <div class='btn' onpointerdown='send(event, ""tmux_sync"")'><div class='icon'>🔄</div>同步输入</div>
                                                    <div class='btn red' onpointerdown='send(event, ""tmux_detach"")'><div class='icon'>🔌</div>分离会话 (d)</div>
                                                </div>
                                            </div>
                                        </div>

                                        <!-- 🌟 全屏遮罩层 -->
                                        <div id='overlay-container' class='overlay-container'>
                                            <div class='close-btn' onpointerdown='closeOverlay()'>❌ 退出全屏</div>
                                            <!-- 摇杆模式 -->
                                            <div id='joystick-ui' style='width:100%; height:100%; flex-direction:column; justify-content:center; align-items:center; display:none;'>
                                                <h2 style='color:#A074E5; margin-bottom: 30px;'>🎮 物理外设手柄模式 (1ms 延迟)</h2>
                                                <div style='display:flex; justify-content:space-between; align-items:center; width:100%; max-width:800px; padding: 20px 40px;'>
                                                    <div class='joystick-zone' id='joystick-zone'><div class='joystick-knob' id='joystick-knob'></div></div>
                                                    <div style='position:relative; width:180px; height:180px;'>
                                                        <div class='action-btn' style='top:-60px; left:20px; width:140px; height:45px; border-radius:22px;' onpointerdown='sendKey(""key_shift"",1)' onpointerup='sendKey(""key_shift"",0)' onpointercancel='sendKey(""key_shift"",0)'>L1 (Shift)</div>
                                                        <div class='action-btn' style='top:0; left:60px;' onpointerdown='sendKey(""key_i"",1)' onpointerup='sendKey(""key_i"",0)' onpointercancel='sendKey(""key_i"",0)'>I</div>
                                                        <div class='action-btn' style='top:60px; left:0;' onpointerdown='sendKey(""key_j"",1)' onpointerup='sendKey(""key_j"",0)' onpointercancel='sendKey(""key_j"",0)'>J</div>
                                                        <div class='action-btn' style='top:60px; left:120px;' onpointerdown='sendKey(""key_l"",1)' onpointerup='sendKey(""key_l"",0)' onpointercancel='sendKey(""key_l"",0)'>L</div>
                                                        <div class='action-btn' style='top:120px; left:60px;' onpointerdown='sendKey(""key_k"",1)' onpointerup='sendKey(""key_k"",0)' onpointercancel='sendKey(""key_k"",0)'>K</div>
                                                        <div class='action-btn' style='top:180px; left:20px; width:140px; height:45px; border-radius:22px; background:#00CA72; border-color:#00CA72;' onpointerdown='sendKey(""key_space"",1)' onpointerup='sendKey(""key_space"",0)' onpointercancel='sendKey(""key_space"",0)'>R1 (空格)</div>
                                                    </div>
                                                </div>
                                            </div>
                                            <!-- 🌟 104全键位满血版 QWERTY 键盘模式 -->
                                            <div id='keyboard-ui' style='width:100%; height:100%; flex-direction:column; justify-content:center; align-items:center; padding-top:20px; display:none;'></div>
                                            
                                            <!-- 🌟 Docker 次级控制面板 -->
                                            <div id='docker-ui' style='width:100%; height:100%; flex-direction:column; align-items:center; justify-content:center; gap: 20px; display: none;'>
                                                <h2 style='margin-bottom: 10px; color: white;'>🐳 Docker 控制中枢</h2>
                                                <div style='display: grid; grid-template-columns: 1fr 1fr; gap: 15px; width: 100%; max-width: 400px;'>
                                                    <div class='btn green' onpointerdown='send(event, ""docker_restart_db"")'><div class='icon'>🗄️</div>重启 MySQL</div>
                                                    <div class='btn green' onpointerdown='send(event, ""docker_restart_redis"")'><div class='icon'>⚡</div>重启 Redis</div>
                                                    <div class='btn red' onpointerdown='send(event, ""docker_stop_all"")'><div class='icon'>🛑</div>停止所有</div>
                                                    <div class='btn' onpointerdown='send(event, ""docker_prune"")'><div class='icon'>🧹</div>系统清理</div>
                                                </div>
                                            </div>
                                            
                                            <!-- 🌟 Linux/DevTools 次级控制面板 -->
                                            <div id='devtools-ui' style='width:100%; height:100%; flex-direction:column; align-items:center; justify-content:center; gap: 20px; display: none; overflow-y: auto; padding-bottom: 30px;'>
                                                <h2 style='margin-bottom: 10px; color: white;'>🐧 Linux 神器快捷键</h2>
                                                
                                                <h3 style='color: #00CA72; margin: 5px 0;'>Vim 编辑器</h3>
                                                <div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 15px; width: 100%; max-width: 500px; margin-bottom: 15px;'>
                                                    <div class='btn' onpointerdown='send(event, ""vim_wq"")'><div class='icon'>💾</div>保存并退出</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_q_bang"")'><div class='icon'>🚪</div>强制退出</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_dd"")'><div class='icon'>✂️</div>删除当前行</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_u"")'><div class='icon'>↩️</div>撤销 (u)</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_search"")'><div class='icon'>🔍</div>搜索 (/)</div>
                                                    <div class='btn' onpointerdown='send(event, ""vim_insert"")'><div class='icon'>✍️</div>插入模式 (i)</div>
                                                </div>

                                                <h3 style='color: #00CA72; margin: 5px 0;'>Tmux 终端复用</h3>
                                                <div style='display: grid; grid-template-columns: repeat(3, 1fr); gap: 15px; width: 100%; max-width: 500px;'>
                                                    <div class='btn green' onpointerdown='send(event, ""tmux_new"")'><div class='icon'>🪟</div>新建窗口 (c)</div>
                                                    <div class='btn green' onpointerdown='send(event, ""tmux_vsplit"")'><div class='icon'>⏸️</div>垂直分割 (%)</div>
                                                    <div class='btn green' onpointerdown='send(event, ""tmux_hsplit"")'><div class='icon'>=</div>水平分割 ("")</div>
                                                    <div class='btn' onpointerdown='send(event, ""tmux_next"")'><div class='icon'>⏭️</div>下一个窗口 (n)</div>
                                                    <div class='btn' onpointerdown='send(event, ""tmux_sync"")'><div class='icon'>🔄</div>同步输入</div>
                                                    <div class='btn red' onpointerdown='send(event, ""tmux_detach"")'><div class='icon'>🔌</div>分离会话 (d)</div>
                                                </div>
                                            </div>

                                            <!-- 🌟 剪贴板历史控制台 -->
                                            <div id='clipboard-ui' style='width:100%; height:100%; flex-direction:column; align-items:center; justify-content:flex-start; gap: 15px; display: none; overflow-y: auto; padding: 20px 0 50px 0;'>
                                                <h2 style='color: white; margin-bottom: 5px;'>📜 剪贴板云同步历史</h2>
                                                <div style='color: #AAA; font-size: 13px; margin-bottom: 15px;'>轻触下方卡片，即可将其安全复制到平板</div>
                                                <div id='clip-history-list' style='width: 100%; max-width: 500px; display: flex; flex-direction: column; gap: 12px;'></div>
                                            </div>
                                        </div>
                                </body></html>".Replace("DECK_CONFIG_PAYLOAD", GetOrCreateDeckConfig()).Replace("AUTH_TOKEN_PAYLOAD", _deckAuthToken); // 🌟 注入本地磁盘配置与安全令牌
                                byte[] buffer = Encoding.UTF8.GetBytes(html);
                                res.ContentType = "text/html; charset=utf-8";
                                await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                                res.Close();
                            }
                        });
                    }
                    catch { }
                }
            }, token);
        }

        public void StopStreamDeckServer()
        {
            _streamDeckCts?.Cancel();
            _streamDeckServer?.Stop();
        }

        // 🌟 闪电剪贴板引擎：突破 API 限制，利用剪贴板实现超长 LaTeX 和咒语的瞬间输入
        private void PasteText(string text)
        {
            Thread thread = new Thread(() => {
                Clipboard.SetText(text);
                Thread.Sleep(50); // 等待剪贴板就绪
                keybd_event(0x11, 0, 0, 0); // 按下 Ctrl
                keybd_event(0x56, 0, 0, 0); // 按下 V
                keybd_event(0x56, 0, KEYEVENTF_KEYUP, 0); // 松开 V
                keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); // 松开 Ctrl
            });
            thread.SetApartmentState(ApartmentState.STA); // 剪贴板必须在 STA 线程中运行
            thread.Start();
            thread.Join();
        }

        // 🌟 防封号安全机制：带有随机抖动的睡眠函数，模拟人类真实的肌肉反应
        private void SafeSleep(int minMs, int maxMs)
        {
            Thread.Sleep(new Random().Next(minMs, maxMs));
        }

        // 🌟 原子级外设映射辅助库：用于组装极客复杂的复合快捷键序列
        private void TapKey(byte key) { keybd_event(key, 0, 0, 0); keybd_event(key, 0, KEYEVENTF_KEYUP, 0); }
        private void TapCtrl(byte key) { keybd_event(0x11, 0, 0, 0); TapKey(key); keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); }
        private void TapShift(byte key) { keybd_event(0x10, 0, 0, 0); TapKey(key); keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0); }
        private void TmuxPrefix() { TapCtrl(0x42); Thread.Sleep(50); } // Tmux 默认前缀键 Ctrl+B

        // 🌟 字典大全：把字符串翻译成 Windows 虚拟键码 (Virtual Key Code)
        private byte GetVirtualKey(string k)
        {
            k = k.ToLower();
            if (k.Length == 1) {
                char c = char.ToUpper(k[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return (byte)c;
                if (k == "`" || k == "~") return 0xC0;
                if (k == "-") return 0xBD;
                if (k == "=") return 0xBB;
                if (k == "[") return 0xDB;
                if (k == "]") return 0xDD;
                if (k == "\\") return 0xDC;
                if (k == ";") return 0xBA;
                if (k == "'") return 0xDE;
                if (k == ",") return 0xBC;
                if (k == ".") return 0xBE;
                if (k == "/") return 0xBF;
            }
            switch (k)
            {
                case "space": return 0x20;   case "shift": return 0x10;
                case "ctrl": return 0x11;    case "alt": return 0x12;
                case "win": return 0x5B;     case "enter": return 0x0D;
                case "esc": return 0x1B;     case "tab": return 0x09;
                case "caps": return 0x14;    case "backspace": return 0x08;
                case "up": return 0x26;      case "down": return 0x28;
                case "left": return 0x25;    case "right": return 0x27;
            }
            if (k.StartsWith("f") && int.TryParse(k.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12) return (byte)(0x6F + fNum);
            return 0;
        }

        private void ExecuteMacro(string cmd)
        {
            // 🌟 动态解析引擎：运行任意 EXE 或 脚本
            if (cmd.StartsWith("run|")) {
                string path = cmd.Substring(4).Trim('"', ' ');
                if (File.Exists(path)) {
                    Process.Start(new ProcessStartInfo { FileName = path, WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true });
                } else {
                    Dispatcher.Invoke(() => MessageBox.Show("找不到指定的文件路径: \n" + path, "路径错误"));
                }
                return;
            }

            // 🌟 动态解析引擎：打开任意网址或邮箱 mailto:
            if (cmd.StartsWith("url|")) {
                Process.Start(new ProcessStartInfo { FileName = cmd.Substring(4).Trim(), UseShellExecute = true });
                return;
            }

            // 🌟 动态解析引擎：自动模拟复合快捷键 (如 Ctrl+Shift+C)
            if (cmd.StartsWith("hotkey|")) {
                string[] parts = cmd.Substring(7).Trim().ToUpper().Split('+');
                byte key = 0; bool ctrl = false, shift = false, alt = false;
                foreach(var p in parts) {
                    if (p == "CTRL") ctrl = true;
                    else if (p == "SHIFT") shift = true;
                    else if (p == "ALT") alt = true;
                    else if (p.Length == 1) key = (byte)p[0];
                }
                if (ctrl) keybd_event(0x11, 0, 0, 0);
                if (shift) keybd_event(0x10, 0, 0, 0);
                if (alt) keybd_event(0x12, 0, 0, 0);
                
                if (key != 0) { keybd_event(key, 0, 0, 0); keybd_event(key, 0, KEYEVENTF_KEYUP, 0); }
                
                if (alt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);
                if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0);
                if (ctrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);
                return;
            }

            // 🌟 动态解析引擎：带着参数的原神自动化宏！
            if (cmd.StartsWith("genshin_daily|")) {
                string gamePath = cmd.Substring(14).Trim('"', ' ');
                Task.Run(() => {
                    try {
                        if (!File.Exists(gamePath)) {
                            Dispatcher.Invoke(() => MessageBox.Show("找不到原神本体: \n" + gamePath, "路径错误"));
                            return;
                        }
                        Process.Start(new ProcessStartInfo { FileName = gamePath, WorkingDirectory = Path.GetDirectoryName(gamePath), UseShellExecute = true });
                        SafeSleep(40000, 45000);
                        keybd_event(0x0D, 0, 0, 0); SafeSleep(80, 150); keybd_event(0x0D, 0, KEYEVENTF_KEYUP, 0);
                        SafeSleep(15000, 18000);
                        keybd_event(0x70, 0, 0, 0); SafeSleep(80, 150); keybd_event(0x70, 0, KEYEVENTF_KEYUP, 0);
                        SafeSleep(1500, 2000);
                        int screenW = (int)SystemParameters.PrimaryScreenWidth;
                        int screenH = (int)SystemParameters.PrimaryScreenHeight;
                        SetCursorPos((int)(screenW * 0.92), (int)(screenH * 0.88));
                        SafeSleep(150, 300);
                        mouse_event(0x02, 0, 0, 0, 0); SafeSleep(80, 150); mouse_event(0x04, 0, 0, 0, 0);
                    } catch (Exception ex) { Dispatcher.Invoke(() => MessageBox.Show("原神启动失败:\n" + ex.Message)); }
                });
                return;
            }

            // 🌟 动态解析引擎：运行任意 EXE 或 脚本
            if (cmd.StartsWith("run|")) {
                string path = cmd.Substring(4).Trim('"', ' ');
                if (File.Exists(path)) {
                    Process.Start(new ProcessStartInfo { FileName = path, WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true });
                } else {
                    Dispatcher.Invoke(() => MessageBox.Show("找不到指定的文件路径: \n" + path, "路径错误"));
                }
                return;
            }

            // 🌟 动态解析引擎：打开任意网址或邮箱 mailto:
            if (cmd.StartsWith("url|")) {
                Process.Start(new ProcessStartInfo { FileName = cmd.Substring(4).Trim(), UseShellExecute = true });
                return;
            }

            // 🌟 动态解析引擎：自动模拟复合快捷键 (如 Ctrl+Shift+C)
            if (cmd.StartsWith("hotkey|")) {
                string[] parts = cmd.Substring(7).Trim().ToUpper().Split('+');
                byte key = 0; bool ctrl = false, shift = false, alt = false;
                foreach(var p in parts) {
                    if (p == "CTRL") ctrl = true;
                    else if (p == "SHIFT") shift = true;
                    else if (p == "ALT") alt = true;
                    else if (p.Length == 1) key = (byte)p[0];
                }
                if (ctrl) keybd_event(0x11, 0, 0, 0);
                if (shift) keybd_event(0x10, 0, 0, 0);
                if (alt) keybd_event(0x12, 0, 0, 0);
                
                if (key != 0) { keybd_event(key, 0, 0, 0); keybd_event(key, 0, KEYEVENTF_KEYUP, 0); }
                
                if (alt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);
                if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0);
                if (ctrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);
                return;
            }

            // 🌟 动态解析引擎：带着参数的原神自动化宏！
            if (cmd.StartsWith("genshin_daily|")) {
                string gamePath = cmd.Substring(14).Trim('"', ' ');
                Task.Run(() => {
                    try {
                        if (!File.Exists(gamePath)) {
                            Dispatcher.Invoke(() => MessageBox.Show("找不到原神本体: \n" + gamePath, "路径错误"));
                            return;
                        }
                        Process.Start(new ProcessStartInfo { FileName = gamePath, WorkingDirectory = Path.GetDirectoryName(gamePath), UseShellExecute = true });
                        SafeSleep(40000, 45000);
                        keybd_event(0x0D, 0, 0, 0); SafeSleep(80, 150); keybd_event(0x0D, 0, KEYEVENTF_KEYUP, 0);
                        SafeSleep(15000, 18000);
                        keybd_event(0x70, 0, 0, 0); SafeSleep(80, 150); keybd_event(0x70, 0, KEYEVENTF_KEYUP, 0);
                        SafeSleep(1500, 2000);
                        int screenW = (int)SystemParameters.PrimaryScreenWidth;
                        int screenH = (int)SystemParameters.PrimaryScreenHeight;
                        SetCursorPos((int)(screenW * 0.92), (int)(screenH * 0.88));
                        SafeSleep(150, 300);
                        mouse_event(0x02, 0, 0, 0, 0); SafeSleep(80, 150); mouse_event(0x04, 0, 0, 0, 0);
                    } catch (Exception ex) { Dispatcher.Invoke(() => MessageBox.Show("原神启动失败:\n" + ex.Message)); }
                });
                return;
            }

            // 🌟 终极物理按键模拟引擎：加入硬件扫描码 (Scan Code) 防拦截！
            if (cmd.StartsWith("key_"))
            {
                bool isDown = cmd.EndsWith("_down");
                string k = cmd.Substring(4, cmd.Length - (isDown ? 9 : 7));
                byte vk = GetVirtualKey(k);
                if (vk != 0) {
                    uint scanCode = MapVirtualKey(vk, 0); // 提取硬件底层扫描码
                    uint flags = (isDown ? 0 : (uint)KEYEVENTF_KEYUP) | KEYEVENTF_SCANCODE;
                    keybd_event(vk, (byte)scanCode, flags, 0); // 物理级击穿所有游戏护盾！
                }
                return;
            }

            switch (cmd)
            {
                // --- Media (多媒体与系统) ---
                case "vol_mute": keybd_event(0xAD, 0, KEYEVENTF_EXTENDEDKEY, 0); keybd_event(0xAD, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); break;
                case "vol_down": keybd_event(0xAE, 0, KEYEVENTF_EXTENDEDKEY, 0); keybd_event(0xAE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); break;
                case "vol_up":   keybd_event(0xAF, 0, KEYEVENTF_EXTENDEDKEY, 0); keybd_event(0xAF, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); break;
                case "media_prev": keybd_event(0xB1, 0, KEYEVENTF_EXTENDEDKEY, 0); keybd_event(0xB1, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); break;
                case "media_play": keybd_event(0xB3, 0, KEYEVENTF_EXTENDEDKEY, 0); keybd_event(0xB3, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); break;
                case "media_next": keybd_event(0xB0, 0, KEYEVENTF_EXTENDEDKEY, 0); keybd_event(0xB0, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0); break;
                case "win_d": keybd_event(0x5B, 0, 0, 0); keybd_event(0x44, 0, 0, 0); keybd_event(0x44, 0, KEYEVENTF_KEYUP, 0); keybd_event(0x5B, 0, KEYEVENTF_KEYUP, 0); break;
                case "lock": Process.Start(new ProcessStartInfo { FileName = "rundll32.exe", Arguments = "user32.dll,LockWorkStation", UseShellExecute = true }); break;

                // --- Dev (开发) ---
                case "dev_vscode": 
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c code", UseShellExecute = true, CreateNoWindow = true }); 
                    break;
                case "dev_term":
                    Process.Start(new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = true }); // 启动 Windows Terminal
                    break;
                case "dev_taskmgr":
                    Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
                    break;
                case "dev_git_sync":
                    PasteText("git pull && git push");
                    Thread.Sleep(100); 
                    keybd_event(0x0D, 0, 0, 0); // 敲下回车键
                    keybd_event(0x0D, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "docker_restart_db":
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c docker restart mysql", UseShellExecute = true, CreateNoWindow = true }); 
                    break;
                case "docker_restart_redis":
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c docker restart redis", UseShellExecute = true, CreateNoWindow = true }); 
                    break;
                case "docker_stop_all":
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c FOR /f \"tokens=*\" %i IN ('docker ps -q') DO docker stop %i", UseShellExecute = true, CreateNoWindow = true }); 
                    break;
                case "docker_prune":
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c docker system prune -f", UseShellExecute = true, CreateNoWindow = true }); 
                    break;

                // --- Linux & DevTools (Vim / Tmux 神器连招) ---
                case "vim_wq": TapKey(0x1B); Thread.Sleep(50); TapShift(0xBA); TapKey(0x57); TapKey(0x51); TapKey(0x0D); break; // ESC, :wq, ENTER
                case "vim_q_bang": TapKey(0x1B); Thread.Sleep(50); TapShift(0xBA); TapKey(0x51); TapShift(0x31); TapKey(0x0D); break; // ESC, :q!, ENTER
                case "vim_dd": TapKey(0x1B); Thread.Sleep(50); TapKey(0x44); TapKey(0x44); break; // ESC, dd
                case "vim_u": TapKey(0x1B); Thread.Sleep(50); TapKey(0x55); break; // ESC, u
                case "vim_search": TapKey(0x1B); Thread.Sleep(50); TapKey(0xBF); break; // ESC, /
                case "vim_insert": TapKey(0x1B); Thread.Sleep(50); TapKey(0x49); break; // ESC, i

                case "tmux_new": TmuxPrefix(); TapKey(0x43); break; // Ctrl+B, c
                case "tmux_vsplit": TmuxPrefix(); TapShift(0x35); break; // Ctrl+B, % (Shift+5)
                case "tmux_hsplit": TmuxPrefix(); TapShift(0xDE); break; // Ctrl+B, " (Shift+')
                case "tmux_next": TmuxPrefix(); TapKey(0x4E); break; // Ctrl+B, n
                case "tmux_detach": TmuxPrefix(); TapKey(0x44); break; // Ctrl+B, d
                case "tmux_sync": 
                    TmuxPrefix(); TapShift(0xBA); Thread.Sleep(50); // Ctrl+B, :
                    PasteText("setw synchronize-panes"); Thread.Sleep(100); TapKey(0x0D); break;

                // --- Academic (学术) ---
                case "acad_cite":
                    // 模拟在 Word 中按下 Zotero 的快捷键 (假设你设置了 Ctrl+Alt+C)
                    keybd_event(0x11, 0, 0, 0); // Ctrl
                    keybd_event(0x12, 0, 0, 0); // Alt
                    keybd_event(0x43, 0, 0, 0); // C
                    keybd_event(0x43, 0, KEYEVENTF_KEYUP, 0);
                    keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);
                    keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);
                    break;
                case "acad_matrix":
                    PasteText("\\begin{pmatrix}\n  a & b \\\\\n  c & d\n\\end{pmatrix}");
                    break;
                case "acad_draw":
                    // TODO: 打开我们在前端准备的 Canvas 画布（在未来的交互亮点中实现）
                    MessageBox.Show("即将开启手写转 LaTeX 面板...");
                    break;

                // --- Network (网络操作) ---
                case "net_ip": Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/k ipconfig", UseShellExecute = true }); break;
                case "net_flush": Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c ipconfig /flushdns", UseShellExecute = true }); break;
                case "net_proxy": Process.Start(new ProcessStartInfo { FileName = "ms-settings:network-proxy", UseShellExecute = true }); break;

                // --- Creative (创作宏) ---
                case "cre_undo": keybd_event(0x11, 0, 0, 0); keybd_event(0x5A, 0, 0, 0); keybd_event(0x5A, 0, KEYEVENTF_KEYUP, 0); keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); break; // Ctrl+Z
                case "cre_redo": keybd_event(0x11, 0, 0, 0); keybd_event(0x59, 0, 0, 0); keybd_event(0x59, 0, KEYEVENTF_KEYUP, 0); keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); break; // Ctrl+Y
                case "cre_save": keybd_event(0x11, 0, 0, 0); keybd_event(0x53, 0, 0, 0); keybd_event(0x53, 0, KEYEVENTF_KEYUP, 0); keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); break; // Ctrl+S
                case "cre_brush": keybd_event(0x42, 0, 0, 0); keybd_event(0x42, 0, KEYEVENTF_KEYUP, 0); break; // B
                case "cre_zoomin": keybd_event(0x11, 0, 0, 0); keybd_event(0xBB, 0, 0, 0); keybd_event(0xBB, 0, KEYEVENTF_KEYUP, 0); keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); break; // Ctrl++
                case "cre_zoomout": keybd_event(0x11, 0, 0, 0); keybd_event(0xBD, 0, 0, 0); keybd_event(0xBD, 0, KEYEVENTF_KEYUP, 0); keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0); break; // Ctrl+-
            }
        }
    }
}