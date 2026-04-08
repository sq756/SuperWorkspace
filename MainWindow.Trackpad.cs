using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // ==========================================
        // 模块 9：功能 2 (虚拟触控板引擎)
        // ==========================================

        private HttpListener? _trackpadServer;
        private CancellationTokenSource? _trackpadCts;

        private const uint TP_MOVE = 0x0001;
        private const uint TP_LEFTDOWN = 0x0002;
        private const uint TP_LEFTUP = 0x0004;
        private const uint TP_RIGHTDOWN = 0x0008;
        private const uint TP_RIGHTUP = 0x0010;
        private const uint TP_WHEEL = 0x0800;

        private void StartTrackpadServer(int port, double sensitivity = 2.5)
        {
            _trackpadCts = new CancellationTokenSource();
            var token = _trackpadCts.Token;
            
            _trackpadServer = new HttpListener();
            _trackpadServer.Prefixes.Add($"http://localhost:{port}/");
            _trackpadServer.Start();

            Task.Run(async () => 
            {
                while (!token.IsCancellationRequested && _trackpadServer.IsListening)
                {
                    try
                    {
                        var context = await _trackpadServer.GetContextAsync();
                        _ = Task.Run(async () => 
                        {
                            if (context.Request.IsWebSocketRequest) 
                            {
                                var wsContext = await context.AcceptWebSocketAsync(null);
                                var ws = wsContext.WebSocket;
                                byte[] buffer = new byte[1024];
                                
                                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested) 
                                {
                                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                                    if (result.MessageType == WebSocketMessageType.Text) 
                                    {
                                        string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                        using JsonDocument doc = JsonDocument.Parse(msg);
                                        string cmd = doc.RootElement.GetProperty("cmd").GetString() ?? "";
                                        
                                        if (cmd == "move") {
                                            int dx = (int)(doc.RootElement.GetProperty("dx").GetDouble() * sensitivity); 
                                            int dy = (int)(doc.RootElement.GetProperty("dy").GetDouble() * sensitivity); 
                                            mouse_event(TP_MOVE, (uint)dx, (uint)dy, 0, 0);
                                        }
                                        else if (cmd == "left_click") {
                                            mouse_event(TP_LEFTDOWN, 0, 0, 0, 0);
                                            mouse_event(TP_LEFTUP, 0, 0, 0, 0);
                                        }
                                        else if (cmd == "right_click") {
                                            mouse_event(TP_RIGHTDOWN, 0, 0, 0, 0);
                                            mouse_event(TP_RIGHTUP, 0, 0, 0, 0);
                                        }
                                        else if (cmd == "scroll") {
                                            int dy = (int)(doc.RootElement.GetProperty("dy").GetDouble() * 4); // 滚轮速度倍率
                                            mouse_event(TP_WHEEL, 0, 0, (uint)dy, 0);
                                        }
                                        else if (cmd == "drag_start") { // 🌟 拖拽开始：按下左键
                                            mouse_event(TP_LEFTDOWN, 0, 0, 0, 0);
                                        }
                                        else if (cmd == "drag_end") { // 🌟 拖拽结束：松开左键
                                            mouse_event(TP_LEFTUP, 0, 0, 0, 0);
                                        }
                                        else if (cmd == "zoom") { // 双指缩放
                                            int dy = (int)doc.RootElement.GetProperty("dy").GetDouble();
                                            keybd_event(0x11, 0, 0, 0); // Ctrl 按下
                                            mouse_event(TP_WHEEL, 0, 0, (uint)dy, 0);
                                            Thread.Sleep(10); // 🌟 稍微等一下，确保鼠标滚轮事件被消化
                                            keybd_event(0x11, 0, 0x0002, 0); // Ctrl 抬起
                                        }
                                        else if (cmd == "copy") { // 四指上滑：复制
                                            keybd_event(0x11, 0, 0, 0); keybd_event(0x43, 0, 0, 0);
                                            Thread.Sleep(30); // 🌟 核心防粘滞：等待 OS 消化按键，防止 Ctrl 键卡死吞掉鼠标
                                            keybd_event(0x43, 0, 0x0002, 0); keybd_event(0x11, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "paste") { // 四指下滑：粘贴
                                            keybd_event(0x11, 0, 0, 0); keybd_event(0x56, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x56, 0, 0x0002, 0); keybd_event(0x11, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "screenshot") { // 五指抓取：截屏
                                            keybd_event(0x5B, 0, 0, 0); keybd_event(0x10, 0, 0, 0); keybd_event(0x53, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x53, 0, 0x0002, 0); keybd_event(0x10, 0, 0x0002, 0); keybd_event(0x5B, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "search") { // 三指轻敲：打开搜索
                                            keybd_event(0x5B, 0, 0, 0); keybd_event(0x53, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x53, 0, 0x0002, 0); keybd_event(0x5B, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "task_view") { // 三指上滑：任务视图
                                            keybd_event(0x5B, 0, 0, 0); keybd_event(0x09, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x09, 0, 0x0002, 0); keybd_event(0x5B, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "alt_tab") { // 三指侧滑：切换下一个应用
                                            keybd_event(0x12, 0, 0, 0); keybd_event(0x09, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x09, 0, 0x0002, 0); keybd_event(0x12, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "alt_shift_tab") { // 三指侧滑：切换上一个应用
                                            keybd_event(0x12, 0, 0, 0); keybd_event(0x10, 0, 0, 0); keybd_event(0x09, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x09, 0, 0x0002, 0); keybd_event(0x10, 0, 0x0002, 0); keybd_event(0x12, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "desk_left") { // 四指右滑：向左切换桌面
                                            keybd_event(0x5B, 0, 0, 0); keybd_event(0x11, 0, 0, 0); keybd_event(0x25, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x25, 0, 0x0002, 0); keybd_event(0x11, 0, 0x0002, 0); keybd_event(0x5B, 0, 0x0002, 0);
                                        }
                                        else if (cmd == "desk_right") { // 四指左滑：向右切换桌面
                                            keybd_event(0x5B, 0, 0, 0); keybd_event(0x11, 0, 0, 0); keybd_event(0x27, 0, 0, 0);
                                            Thread.Sleep(30);
                                            keybd_event(0x27, 0, 0x0002, 0); keybd_event(0x11, 0, 0x0002, 0); keybd_event(0x5B, 0, 0x0002, 0);
                                        }
                                    }
                                }
                                return;
                            }
                            
                            if (context.Request.Url?.AbsolutePath == "/") 
                            {
                                string html = @"<html><head>
                                    <meta name='viewport' content='width=device-width, initial-scale=1.0, user-scalable=no'>
                                    <style>
                                        body { background: #121212; color: #888; margin: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100vh; touch-action: none; font-family: 'Segoe UI', sans-serif; user-select: none; }
                                        .hint { font-size: 28px; font-weight: bold; margin-bottom: 20px; color: #00CA72; }
                                        .sub { font-size: 16px; margin: 5px 0; background: #252526; padding: 10px 20px; border-radius: 8px; border: 1px solid #333; }
                                    </style>
                                    </head><body>
                                    <div class='hint' id='hint-title' style='cursor:pointer;'>🪄 Magic Trackpad</div>
                                    <div id='hint-box' style='display:flex; flex-direction:column; align-items:center;'>
                                        <div class='sub'>👆 单指滑动 = 移动光标</div>
                                        <div class='sub'>🖱️ 一/二/三指敲击 = 左/右键/搜索</div>
                                        <div class='sub'>🔤 单指双击并滑动 = 选中文字/拖拽</div>
                                        <div class='sub'>↕️ 双指滑动/捏合 = 滚轮 / 缩放</div>
                                        <div class='sub'>🖐️ 三指滑动 = 任务视图 / 切App</div>
                                        <div class='sub'>✋ 四指左右/上下滑 = 切桌面 / 复制粘贴</div>
                                        <div class='sub'>🦅 五指聚拢抓取 = 区域截屏</div>
                                    </div>
                                    <script>
                                        // 🌟 交互：点击标题自动折叠提示框
                                        document.getElementById('hint-title').addEventListener('pointerdown', e => {
                                            e.stopPropagation();
                                            let box = document.getElementById('hint-box');
                                            box.style.display = box.style.display === 'none' ? 'flex' : 'none';
                                        });

                                        var ws = new WebSocket('ws://' + location.host + '/ws');
                                        var touches = new Map(); var isMoved = false;
                                        var startX = 0, startY = 0; var gestureFired = false;
                                        var maxTouches = 0; // 🌟 新增：记录单次交互中出现的最多手指数量
                                        // 🌟 新增防误触变量：双指与五指手势判断
                                        var startDist = 0; var gestureType = 'none'; 
                                        var startDist5 = 0;
                                        var lastTapTime = 0; // 🌟 记录上一次敲击的时间
                                        var isDragging = false; // 🌟 拖拽状态锁

                                        document.body.addEventListener('pointerdown', e => { 
                                            document.documentElement.requestFullscreen().catch(()=>{}); 
                                            touches.set(e.pointerId, {x: e.clientX, y: e.clientY, time: Date.now()}); 
                                            
                                            if (touches.size > maxTouches) maxTouches = touches.size; // 🌟 记录最高指头数
                                            if (touches.size === 1) isMoved = false; // 🌟 仅在第一根手指落下时重置移动判定
                                            if (touches.size === 1) {
                                                isMoved = false; // 🌟 仅在第一根手指落下时重置移动判定
                                                // 🌟 判定双击拖拽：两次点击间隔小于 400ms，触发拖拽模式！
                                                if (Date.now() - lastTapTime < 400) {
                                                    isDragging = true;
                                                    if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({cmd: 'drag_start'}));
                                                }
                                            } else if (isDragging) {
                                                // 🌟 如果在拖拽途中落下了第二根手指，为了防误触，安全中断拖拽
                                                isDragging = false;
                                                if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({cmd: 'drag_end'}));
                                            }

                                            let cx = 0, cy = 0; touches.forEach(t => { cx += t.x; cy += t.y; });
                                            startX = cx / touches.size; startY = cy / touches.size;
                                            if (touches.size >= 3) gestureFired = false;
                                            
                                            // 🌟 双指测距初始化
                                            if (touches.size === 2) {
                                                let pts = Array.from(touches.values());
                                                startDist = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
                                                gestureType = 'none'; // 重置状态锁
                                            }
                                            // 🌟 五指测距初始化 (计算五指到中心点的平均距离)
                                            if (touches.size === 5) {
                                                let pts = Array.from(touches.values());
                                                let avgX = pts.reduce((a, b) => a + b.x, 0) / 5; let avgY = pts.reduce((a, b) => a + b.y, 0) / 5;
                                                startDist5 = pts.reduce((a, b) => a + Math.hypot(b.x - avgX, b.y - avgY), 0) / 5;
                                            }
                                        });

                                        document.body.addEventListener('pointermove', e => { 
                                            if (!touches.has(e.pointerId)) return; 
                                            let last = touches.get(e.pointerId); 
                                            let dx = e.clientX - last.x; let dy = e.clientY - last.y; 
                                            touches.set(e.pointerId, {x: e.clientX, y: e.clientY, time: last.time}); 
                                            if (Math.abs(dx) > 1 || Math.abs(dy) > 1) isMoved = true; 
                                            if (ws.readyState !== WebSocket.OPEN) return;

                                            if (touches.size === 1) { ws.send(JSON.stringify({cmd: 'move', dx: dx, dy: dy})); } 
                                            else if (touches.size === 2) { 
                                                let pts = Array.from(touches.values());
                                                let curDist = Math.hypot(pts[0].x - pts[1].x, pts[0].y - pts[1].y);
                                                // 🌟 动态防误触：一旦触发捏合(剧烈形变)就锁定为 zoom，否则平稳滑动锁定为 scroll
                                                if (gestureType !== 'scroll' && Math.abs(curDist - startDist) > 30) {
                                                    gestureType = 'zoom';
                                                    ws.send(JSON.stringify({cmd: 'zoom', dy: (curDist - startDist) > 0 ? 120 : -120}));
                                                    startDist = curDist; // 连续缩放
                                                } else if (gestureType !== 'zoom' && (Math.abs(dx) > 5 || Math.abs(dy) > 5)) {
                                                    gestureType = 'scroll'; ws.send(JSON.stringify({cmd: 'scroll', dy: -dy}));
                                                }
                                            } 
                                            else if (touches.size === 3 || touches.size === 4) {
                                                if (gestureFired) return; // 触发过一次后必须抬起手指重置
                                                let cx = 0, cy = 0; touches.forEach(t => { cx += t.x; cy += t.y; });
                                                cx /= touches.size; cy /= touches.size;
                                                let tdx = cx - startX; let tdy = cy - startY;

                                                if (touches.size === 3) {
                                                    if (tdy < -80) { ws.send(JSON.stringify({cmd: 'task_view'})); gestureFired = true; }
                                                    else if (tdx < -80) { ws.send(JSON.stringify({cmd: 'alt_tab'})); gestureFired = true; }
                                                    else if (tdx > 80) { ws.send(JSON.stringify({cmd: 'alt_shift_tab'})); gestureFired = true; }
                                                } else if (touches.size === 4) {
                                                    if (tdy < -80) { ws.send(JSON.stringify({cmd: 'copy'})); gestureFired = true; }
                                                    else if (tdy > 80) { ws.send(JSON.stringify({cmd: 'paste'})); gestureFired = true; }
                                                    else if (tdx < -80) { ws.send(JSON.stringify({cmd: 'desk_right'})); gestureFired = true; }
                                                    else if (tdx > 80) { ws.send(JSON.stringify({cmd: 'desk_left'})); gestureFired = true; }
                                                }
                                            } else if (touches.size === 5) {
                                                if (gestureFired) return;
                                                let pts = Array.from(touches.values());
                                                let avgX = pts.reduce((a, b) => a + b.x, 0) / 5; let avgY = pts.reduce((a, b) => a + b.y, 0) / 5;
                                                let curDist5 = pts.reduce((a, b) => a + Math.hypot(b.x - avgX, b.y - avgY), 0) / 5;
                                                if (startDist5 - curDist5 > 40) { // 🌟 距离急剧缩小 = 五指聚拢/抓取
                                                    ws.send(JSON.stringify({cmd: 'screenshot'})); gestureFired = true;
                                                }
                                            } 
                                        });

                                        document.body.addEventListener('pointerup', e => { 
                                            let last = touches.get(e.pointerId); touches.delete(e.pointerId); 
                                            if (touches.size === 0) {
                                                // 1. 优先结算并释放拖拽状态，绝不能卡死鼠标
                                                if (isDragging) {
                                                    if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({cmd: 'drag_end'}));
                                                    isDragging = false;
                                                    lastTapTime = 0; 
                                                } 
                                                // 2. 如果没怎么移动，且没触发其他复杂手势，就算作普通敲击
                                                else if (!isMoved && Date.now() - last.time < 400 && !gestureFired) { 
                                                    if (maxTouches === 1) {
                                                        ws.send(JSON.stringify({cmd: 'left_click'})); 
                                                        lastTapTime = Date.now(); // 记录单击时间，为后面的双击拖拽铺垫
                                                    }
                                                    else if (maxTouches === 2) ws.send(JSON.stringify({cmd: 'right_click'})); 
                                                    else if (maxTouches === 3) ws.send(JSON.stringify({cmd: 'search'})); 
                                                } 
                                                // 3. 🌟 绝对保证清零：无论刚才干了什么，所有手指离开后，记录统统重置！
                                                maxTouches = 0; 
                                            }
                                        });
                                        document.body.addEventListener('pointercancel', e => { 
                                            touches.delete(e.pointerId); 
                                            if (isDragging) {
                                                if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({cmd: 'drag_end'}));
                                                isDragging = false;
                                            }
                                            if (touches.size === 0) maxTouches = 0; 
                                        });
                                        window.oncontextmenu = e => e.preventDefault();
                                    </script></body></html>";
                                byte[] buf = Encoding.UTF8.GetBytes(html);
                                context.Response.ContentType = "text/html; charset=utf-8";
                                await context.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
                                context.Response.Close();
                            }
                        });
                    }
                    catch { }
                }
            }, token);
        }

        public void StopTrackpadServer()
        {
            _trackpadCts?.Cancel();
            _trackpadServer?.Stop();
        }
    }
}