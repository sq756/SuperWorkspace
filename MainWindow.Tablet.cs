using System;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // ==========================================
        // 模块 11：功能 9 (Graphics Tablet 压感数位板引擎)
        // ==========================================

        private HttpListener? _tabletServer;
        private CancellationTokenSource? _tabletCts;
        private double _pressureCurveK = 1.0;
        private IntPtr _synthPenDevice = IntPtr.Zero; // 🌟 新增：原生虚拟画笔设备句柄

        // 🌟 阶段 3 准备：VMulti 内核数据结构定义
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct VMultiDigitizerReport
        {
            public byte ReportID;    // 必须是 VMulti 固件里定义的 Digitizer Report ID (如 0x01)
            public byte Status;      // 0=悬空, 1=接触屏幕 (Tip Switch)
            public ushort X;         // 绝对坐标 X (0-32767)
            public ushort Y;         // 绝对坐标 Y (0-32767)
            public ushort Pressure;  // 压感 (0-65535)
        }

        // 🌟 终极降维打击：直接调用 Windows 原生隐藏 API 捏造物理画笔，无需任何第三方驱动！
        private void InjectPenFromNetwork(byte action, float x, float y, uint pressure)
        {
            if (_synthPenDevice == IntPtr.Zero) return;

            int targetX = (int)(x * SystemParameters.PrimaryScreenWidth);
            int targetY = (int)(y * SystemParameters.PrimaryScreenHeight);

            var penInfo = new PenInjector.POINTER_TYPE_INFO_PEN();
            penInfo.type = PenInjector.PT_PEN;
            
            penInfo.penInfo.pointerInfo.pointerType = PenInjector.PT_PEN;
            penInfo.penInfo.pointerInfo.pointerId = 0;
            penInfo.penInfo.pointerInfo.ptPixelLocation.x = targetX;
            penInfo.penInfo.pointerInfo.ptPixelLocation.y = targetY;
            
            penInfo.penInfo.penMask = PenInjector.PEN_MASK_PRESSURE;
            penInfo.penInfo.pressure = pressure;

            if (action == 0) // Down
                penInfo.penInfo.pointerInfo.pointerFlags = PenInjector.POINTER_FLAG_INRANGE | PenInjector.POINTER_FLAG_INCONTACT | PenInjector.POINTER_FLAG_DOWN;
            else if (action == 1) // Move
                penInfo.penInfo.pointerInfo.pointerFlags = PenInjector.POINTER_FLAG_INRANGE | PenInjector.POINTER_FLAG_INCONTACT | PenInjector.POINTER_FLAG_UPDATE;
            else if (action == 2) // Up
                penInfo.penInfo.pointerInfo.pointerFlags = PenInjector.POINTER_FLAG_UP;

            PenInjector.InjectSyntheticPointerInput(_synthPenDevice, ref penInfo, 1);
        }

        private void StartGraphicsTabletServer(int port, double curveK, bool palmReject)
        {
            _pressureCurveK = curveK;
            _tabletCts = new CancellationTokenSource();
            var token = _tabletCts.Token;
            
            // 🌟 唤醒系统底层的原生画笔引擎
            if (_synthPenDevice == IntPtr.Zero) {
                _synthPenDevice = PenInjector.CreateSyntheticPointerDevice(PenInjector.PT_PEN, 1, PenInjector.POINTER_FEEDBACK_NONE);
            }

            _tabletServer = new HttpListener();
            _tabletServer.Prefixes.Add($"http://localhost:{port}/");
            _tabletServer.Start();

            Task.Run(async () => 
            {
                while (!token.IsCancellationRequested && _tabletServer.IsListening)
                {
                    try
                    {
                        var context = await _tabletServer.GetContextAsync();
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
                                    
                                    // 🌟 阶段 1：超感二进制协议解析 (13 字节)
                                    if (result.MessageType == WebSocketMessageType.Binary && result.Count >= 13)
                                    {
                                        if (buffer[0] == 0x09) // 0x09 = Graphics Tablet Mode
                                        {
                                            byte action = buffer[1]; // 0:Down, 1:Move, 2:Up
                                            float x = BitConverter.ToSingle(buffer, 3); // 0.0 - 1.0
                                            float y = BitConverter.ToSingle(buffer, 7); // 0.0 - 1.0
                                            ushort rawPressure = BitConverter.ToUInt16(buffer, 11);

                                            // 🌟 阶段 2：压感非线性映射 (The Math)
                                            double normalizedP = rawPressure / 65535.0;
                                            double mappedP = Math.Pow(normalizedP, _pressureCurveK);
                                            uint finalPressure = (uint)(mappedP * 1024.0); // 🌟 Windows 内核标准压感级别是 1024

                                            // 🌟 阶段 3：原生内核级压感注入！(降维打击 VMulti)
                                            InjectPenFromNetwork(action, x, y, finalPressure);
                                        }
                                    }
                                    // 兼容快捷键环传来的文本宏
                                    else if (result.MessageType == WebSocketMessageType.Text)
                                    {
                                        string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                        using JsonDocument doc = JsonDocument.Parse(msg);
                                        string cmd = doc.RootElement.GetProperty("cmd").GetString() ?? "";
                                        Dispatcher.Invoke(() => ExecuteMacro(cmd));
                                    }
                                }
                                return;
                            }
                            
                            // 🎨 阶段 3 终极体验：平板端的“画师模式” UI
                            if (context.Request.Url?.AbsolutePath == "/") 
                            {
                                string html = $@"<html><head>
                                    <meta name='viewport' content='width=device-width, initial-scale=1.0, user-scalable=no, maximum-scale=1.0'>
                                    <style>
                                        body {{ background: #111; margin: 0; overflow: hidden; touch-action: none; font-family: sans-serif; user-select: none; display: flex; }}
                                        /* 绘图主黑板 */
                                        #canvas-zone {{ flex: 1; position: relative; }}
                                        #hud {{ position: absolute; top: 20px; left: 20px; color: #555; font-size: 24px; font-weight: bold; pointer-events: none; }}
                                        /* 实时压感流动槽 */
                                        #pressure-bar-container {{ width: 10px; height: 100vh; background: #222; position: relative; }}
                                        #pressure-bar {{ position: absolute; bottom: 0; width: 100%; height: 0%; background: #8A2BE2; transition: height 0.05s linear; box-shadow: 0 0 15px #8A2BE2; }}
                                        /* 左手快捷键环 */
                                        #shortcut-ring {{ width: 80px; background: #1a1a1a; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 30px; border-right: 1px solid #333; }}
                                        .ring-btn {{ width: 50px; height: 50px; border-radius: 25px; background: #333; display: flex; justify-content: center; align-items: center; color: white; font-size: 20px; cursor: pointer; box-shadow: 0 4px 10px rgba(0,0,0,0.5); }}
                                        .ring-btn:active {{ background: #8A2BE2; transform: scale(0.9); }}
                                    </style>
                                    </head><body>
                                    <div id='shortcut-ring'>
                                        <div class='ring-btn' onpointerdown='sendCmd(""cre_undo"")'>↩️</div>
                                        <div class='ring-btn' onpointerdown='sendCmd(""cre_zoomin"")'>[+]</div>
                                        <div class='ring-btn' onpointerdown='sendCmd(""cre_zoomout"")'>[-]</div>
                                    </div>
                                    <div id='canvas-zone'>
                                        <div id='hud'>🎨 专业压感画板就绪<br><span style='font-size:14px;color:#888'>支持 2048 级压感 / {(palmReject ? "防误触已开启" : "防误触已关闭")}</span></div>
                                    </div>
                                    <div id='pressure-bar-container'><div id='pressure-bar'></div></div>
                                    <script>
                                        var ws = new WebSocket('ws://' + location.host + '/ws');
                                        ws.binaryType = 'arraybuffer';
                                        var pBar = document.getElementById('pressure-bar');
                                        var zone = document.getElementById('canvas-zone');
                                        var palmReject = {palmReject.ToString().ToLower()};

                                        function sendCmd(cmd) {{ if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({{cmd: cmd}})); }}

                                        function sendPenData(e, action) {{
                                            // 🛡️ 防误触蒙版：如果开启了防误触，且当前触摸类型不是触控笔(pen)，直接抛弃！
                                            if (palmReject && e.pointerType !== 'pen') return;

                                            e.preventDefault();
                                            let rect = zone.getBoundingClientRect();
                                            let x = (e.clientX - rect.left) / rect.width;
                                            let y = (e.clientY - rect.top) / rect.height;
                                            if (x < 0 || x > 1 || y < 0 || y > 1) return;

                                            // 提取浏览器硬件级压感 (没有压感则默认 1.0)
                                            let p = e.pressure !== undefined ? e.pressure : 1.0;
                                            if (action === 2) p = 0.0; // 抬起时压感归零

                                            // 渲染流动的压感槽
                                            pBar.style.height = (p * 100) + '%';
                                            
                                            // 封包：[Type(1):0x09] [Action(1)] [ID(1):0] [X(4)] [Y(4)] [Pressure(2)] = 13 bytes
                                            if (ws.readyState === WebSocket.OPEN) {{
                                                let buffer = new ArrayBuffer(13);
                                                let view = new DataView(buffer);
                                                view.setUint8(0, 0x09);
                                                view.setUint8(1, action);
                                                view.setUint8(2, 0);
                                                view.setFloat32(3, x, true);
                                                view.setFloat32(7, y, true);
                                                view.setUint16(11, Math.floor(p * 65535), true);
                                                ws.send(buffer);
                                            }}
                                        }}

                                        zone.addEventListener('pointerdown', e => {{ document.documentElement.requestFullscreen().catch(()=>{{}}); sendPenData(e, 0); }});
                                        zone.addEventListener('pointermove', e => sendPenData(e, 1));
                                        zone.addEventListener('pointerup', e => sendPenData(e, 2));
                                        zone.addEventListener('pointercancel', e => sendPenData(e, 2));
                                        window.oncontextmenu = e => e.preventDefault();
                                    </script></body></html>";
                                byte[] buf = Encoding.UTF8.GetBytes(html);
                                context.Response.ContentType = "text/html; charset=utf-8";
                                await context.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
                                context.Response.Close();
                            }
                        });
                    } catch { }
                }
            }, token);
        }

        public void StopGraphicsTabletServer()
        {
            _tabletCts?.Cancel();
            _tabletServer?.Stop();
        }

        // ==========================================
        // 🌟 原生画笔驱动 P/Invoke 引擎
        // ==========================================
        public static class PenInjector
        {
            public const uint PT_PEN = 3;
            public const uint POINTER_FEEDBACK_NONE = 2;
            public const uint PEN_MASK_PRESSURE = 0x00000001;
            public const uint POINTER_FLAG_INRANGE = 0x00000002;
            public const uint POINTER_FLAG_INCONTACT = 0x00000004;
            public const uint POINTER_FLAG_DOWN = 0x00010000;
            public const uint POINTER_FLAG_UPDATE = 0x00020000;
            public const uint POINTER_FLAG_UP = 0x00040000;

            [DllImport("user32.dll")] public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);
            [DllImport("user32.dll")] public static extern void DestroySyntheticPointerDevice(IntPtr hDevice);
            [DllImport("user32.dll")] public static extern bool InjectSyntheticPointerInput(IntPtr hDevice, ref POINTER_TYPE_INFO_PEN pointerInfo, uint count);

            [StructLayout(LayoutKind.Sequential)] public struct POINTER_TYPE_INFO_PEN {
                public uint type; public POINTER_PEN_INFO penInfo;
            }
            [StructLayout(LayoutKind.Sequential)] public struct POINTER_PEN_INFO {
                public TouchInjector.POINTER_INFO pointerInfo;
                public uint penFlags; public uint penMask; public uint pressure;
                public uint rotation; public int tiltX; public int tiltY;
            }
        }
    }
}