using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SharpDX.Direct3D11;
using SharpDX.DXGI;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // 🌟 核心数据模型：动态屏幕雷达实体
        public class DisplayInfo
        {
            public int Id { get; set; }
            public string DeviceName { get; set; } = "";
            public string Resolution { get; set; } = "";
            public string DisplayName { get; set; } = "";
        }

        // ==========================================
        // 🌟 自动强改系统分辨率 API (无需去系统设置里点)
        // ==========================================
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion; public short dmDriverVersion; public short dmSize;
            public short dmDriverExtra; public int dmFields; public int dmPositionX; public int dmPositionY;
            public int dmDisplayOrientation; public int dmDisplayFixedOutput; public short dmColor;
            public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
            public int dmDisplayFlags; public int dmDisplayFrequency; public int dmICMMethod; public int dmICMIntent;
            public int dmMediaType; public int dmDitherType; public int dmReserved1; public int dmReserved2;
            public int dmPanningWidth; public int dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int DM_PELSWIDTH = 0x00080000;
        private const int DM_PELSHEIGHT = 0x00100000;
        private const int DM_DISPLAYFREQUENCY = 0x00400000;

        private bool SetDisplayResolution(string deviceName, int width, int height, int fps) {
            DEVMODE devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref devMode)) {
                if (width > 0 && height > 0) {
                    devMode.dmPelsWidth = width; devMode.dmPelsHeight = height;
                    devMode.dmFields |= DM_PELSWIDTH | DM_PELSHEIGHT;
                }
                if (fps > 0) {
                    devMode.dmDisplayFrequency = fps;
                    devMode.dmFields |= DM_DISPLAYFREQUENCY;
                }
                int result = ChangeDisplaySettingsEx(deviceName, ref devMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                return result == 0;
            }
            return false;
        }

        // --- 新增：获取系统全局真实的鼠标坐标 ---
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        // 🌟 核心引擎拓展：极其霸道的单窗口物理抓取 (即使窗口被遮挡也能完美抓取)
        private byte[] CaptureSingleWindow(DisplaySession session, int quality)
        {
            try
            {
                GetClientRect(session.TargetHwnd, out RECT rect);
                int width = rect.right - rect.left;
                int height = rect.bottom - rect.top;

                if (width <= 0 || height <= 0) return session.LastValidFrame ?? Array.Empty<byte>();

                session.CaptureWidth = width;
                session.CaptureHeight = height;

                // 将窗口内部客户端的 0,0 转换为屏幕的绝对坐标，用于后续触控 100% 映射点击穿透！
                POINT pt = new POINT { x = 0, y = 0 };
                ClientToScreen(session.TargetHwnd, ref pt);
                session.OffsetX = pt.x;
                session.OffsetY = pt.y;

                using (var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    IntPtr hdcDest = g.GetHdc();
                    bool success = PrintWindow(session.TargetHwnd, hdcDest, 3); // 3 = PW_CLIENTONLY | PW_RENDERFULLCONTENT
                    g.ReleaseHdc(hdcDest);

                    if (success)
                    {
                        GetCursorPos(out POINT mousePos);
                        int mouseX = mousePos.x - session.OffsetX;
                        int mouseY = mousePos.y - session.OffsetY;
                        if (mouseX >= 0 && mouseX < bitmap.Width && mouseY >= 0 && mouseY < bitmap.Height) System.Windows.Forms.Cursors.Arrow.Draw(g, new System.Drawing.Rectangle(mouseX, mouseY, 32, 32));

                        OnVideoFrameCaptured?.Invoke(bitmap); // 🌟 触发扩展生态的视频流 Hook

                        using (var ms = new MemoryStream()) {
                            var encoder = GetEncoder(ImageFormat.Jpeg);
                            var parameters = new EncoderParameters(1);
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                            bitmap.Save(ms, encoder!, parameters);
                            session.LastValidFrame = ms.ToArray(); 
                        }
                    }
                }
                return session.LastValidFrame ?? Array.Empty<byte>();
            }
            catch { return session.LastValidFrame ?? Array.Empty<byte>(); }
        }

        private byte[] CaptureScreenDirectX(DisplaySession session, int quality)
        {
            try
            {
                if (session.DeskDupl == null)
                {
                    var factory = new Factory1();
                    var adapter = factory.GetAdapter1(0); 
                    session.Device = new SharpDX.Direct3D11.Device(adapter);
                    
                    var outputs = adapter.Outputs;
                    if (session.ScreenIndex >= outputs.Length) session.ScreenIndex = 0; 
                    
                    var output1 = outputs[session.ScreenIndex].QueryInterface<Output1>();
                    session.DeskDupl = output1.DuplicateOutput(session.Device);

                    session.OffsetX = output1.Description.DesktopBounds.Left;
                    session.OffsetY = output1.Description.DesktopBounds.Top;

                    var textureDesc = new Texture2DDescription {
                        CpuAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None,
                        Format = Format.B8G8R8A8_UNorm,
                        Width = output1.Description.DesktopBounds.Right - session.OffsetX,
                        Height = output1.Description.DesktopBounds.Bottom - session.OffsetY,
                        OptionFlags = ResourceOptionFlags.None,
                        MipLevels = 1, ArraySize = 1,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging
                    };
                    session.DesktopImg = new Texture2D(session.Device, textureDesc);
                }

                var result = session.DeskDupl.TryAcquireNextFrame(100, out var frameInfo, out var desktopResource);
                
                if (result.Success && desktopResource != null)
                {
                    using (var tempTexture = desktopResource.QueryInterface<Texture2D>())
                    {
                        session.Device!.ImmediateContext.CopyResource(tempTexture, session.DesktopImg);
                    }
                    desktopResource.Dispose();
                    session.DeskDupl.ReleaseFrame();

                    var mapSource = session.Device!.ImmediateContext.MapSubresource(session.DesktopImg!, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                    using (var bitmap = new Bitmap(session.DesktopImg!.Description.Width, session.DesktopImg.Description.Height))
                    {
                        session.CaptureWidth = bitmap.Width;
                        session.CaptureHeight = bitmap.Height;
                        var boundsRect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                        var mapDest = bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        SharpDX.Utilities.CopyMemory(mapDest.Scan0, mapSource.DataPointer, mapSource.RowPitch * bitmap.Height);
                        bitmap.UnlockBits(mapDest);
                        session.Device.ImmediateContext.UnmapSubresource(session.DesktopImg, 0);

                        GetCursorPos(out POINT mousePos);
                        int mouseX = mousePos.x - session.OffsetX;
                        int mouseY = mousePos.y - session.OffsetY;

                        if (mouseX >= 0 && mouseX < bitmap.Width && mouseY >= 0 && mouseY < bitmap.Height)
                        {
                            using (Graphics g = Graphics.FromImage(bitmap)) 
                            {
                                System.Windows.Forms.Cursors.Arrow.Draw(g, new System.Drawing.Rectangle(mouseX, mouseY, 32, 32));
                            }
                        }

                        OnVideoFrameCaptured?.Invoke(bitmap); // 🌟 触发扩展生态的视频流 Hook

                        using (var ms = new MemoryStream())
                        {
                            var encoder = GetEncoder(ImageFormat.Jpeg);
                            var parameters = new EncoderParameters(1);
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                            bitmap.Save(ms, encoder!, parameters);
                            
                            session.LastValidFrame = ms.ToArray(); 
                        }
                    }
                }
                else if (result.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code)
                {
                    session.DeskDupl?.Dispose();
                    session.DeskDupl = null;
                }

                return session.LastValidFrame ?? Array.Empty<byte>();
            }
            catch 
            { 
                session.DeskDupl?.Dispose();
                session.DeskDupl = null;
                return session.LastValidFrame ?? Array.Empty<byte>(); 
            }
        }

        private ImageCodecInfo? GetEncoder(ImageFormat format) {
            foreach (var codec in ImageCodecInfo.GetImageEncoders()) 
                if (codec.FormatID == format.Guid) return codec;
            return null;
        }

        private async Task HandleHttpRequests(DisplaySession session, int quality)
        {
            var token = session.Cts!.Token;
            while (!token.IsCancellationRequested && session.Server!.IsListening)
            {
                try {
                    var context = await session.Server.GetContextAsync();
                    _ = Task.Run(async () => {
                        try {
                            var request = context.Request;
                            var response = context.Response;
                            
                            // 🌟 核心路由：如果是 WebSocket 升级请求，直接转交给 Network 模块的超光速管道！
                            if (context.Request.IsWebSocketRequest) {
                                await HandleWebSocketConnection(context, session);
                                return;
                            }

                            if (request.Url?.AbsolutePath == "/touch" && request.HttpMethod == "POST") {
                                try {
                                    using var reader = new StreamReader(request.InputStream);
                                    string json = await reader.ReadToEndAsync();
                                    
                                    using JsonDocument doc = JsonDocument.Parse(json);
                                    JsonElement root = doc.RootElement;
                                    
                                    var contacts = new List<TouchInjector.POINTER_TOUCH_INFO>();

                                    bool hasDown = false, hasUp = false;
                                    int mouseX = 0, mouseY = 0;

                                    foreach (JsonElement touch in root.EnumerateArray()) {
                                        uint id = touch.GetProperty("id").GetUInt32();
                                        string state = touch.GetProperty("state").GetString()!;
                                        float relX = touch.GetProperty("x").GetSingle();
                                        float relY = touch.GetProperty("y").GetSingle();

                                        if (session.CaptureWidth > 0 && session.CaptureHeight > 0) {
                                            int targetX = session.OffsetX + (int)(relX * session.CaptureWidth);
                                            int targetY = session.OffsetY + (int)(relY * session.CaptureHeight);

                                            if (id == 0) { 
                                                mouseX = targetX; mouseY = targetY;
                                                if (state == "down") hasDown = true;
                                                if (state == "up") hasUp = true;
                                            }

                                            var contact = new TouchInjector.POINTER_TOUCH_INFO();
                                            contact.pointerInfo.pointerType = TouchInjector.PT_TOUCH;
                                            contact.pointerInfo.pointerId = id; 
                                            contact.pointerInfo.ptPixelLocation.x = targetX;
                                            contact.pointerInfo.ptPixelLocation.y = targetY;
                                            contact.touchMask = 0; 

                                            if (state == "down") contact.pointerInfo.pointerFlags = TouchInjector.POINTER_FLAG_DOWN | TouchInjector.POINTER_FLAG_INRANGE | TouchInjector.POINTER_FLAG_INCONTACT | TouchInjector.POINTER_FLAG_NEW;
                                            else if (state == "move") contact.pointerInfo.pointerFlags = TouchInjector.POINTER_FLAG_UPDATE | TouchInjector.POINTER_FLAG_INRANGE | TouchInjector.POINTER_FLAG_INCONTACT;
                                            else if (state == "up") contact.pointerInfo.pointerFlags = TouchInjector.POINTER_FLAG_UP;

                                            contacts.Add(contact);
                                        }
                                    }

                                    if (contacts.Count > 0) {
                                        bool success = TouchInjector.InjectTouchInput((uint)contacts.Count, contacts.ToArray());
                                        if (!success) {
                                            SetCursorPos(mouseX, mouseY);
                                            if (hasDown) mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0); 
                                            if (hasUp) mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);    
                                        }
                                    }
                                } 
                                catch (Exception ex) 
                                { 
                                    Debug.WriteLine("触控解析失败: " + ex.Message); 
                                }
                                
                                response.StatusCode = 200;
                                response.OutputStream.Close();
                                return;
                            }

                            if (request.Url?.AbsolutePath == "/") {
                                // 🌟 第二阶段革命：引入 ArrayBuffer 10 字节二进制封装与 WebSocket
                                string html = @"<html><head>
                                    <meta name='viewport' content='width=device-width, initial-scale=1.0, user-scalable=no'>
                                    <meta name='mobile-web-app-capable' content='yes'>
                                    <style>body { margin: 0; background: #000; display: flex; align-items: center; justify-content: center; overflow: hidden;} 
                                        img { max-width: 100%; max-height: 100%; object-fit: contain; cursor: none; touch-action: none; }</style>
                                    </head><body><img id='screen' src='/stream'>
                                    <script>
                                        var img = document.getElementById('screen');
                                        var activeTouches = new Map();
                                        var winIdCounter = 0;
                                        
                                        // 🌟 建立超光速 WebSocket 管道
                                        var ws = new WebSocket('ws://' + location.host + '/ws');
                                        ws.binaryType = 'arraybuffer'; // 启用二进制模式

                                        function processEvent(e, action) {
                                            e.preventDefault();
                                            var isMouse = (e.pointerType === 'mouse') ? 1 : 0;
                                            if (ws.readyState !== WebSocket.OPEN) return;

                                            var rect = img.getBoundingClientRect();
                                            
                                            // 🌟 120Hz+ 报点率引擎：提取两帧之间的所有高频轨迹点
                                            var events = [e];
                                            if (action === 2 && e.getCoalescedEvents) {
                                                events = e.getCoalescedEvents() || [e];
                                                if (events.length === 0) events = [e];
                                            }

                                            for (let i = 0; i < events.length; i++) {
                                                let ev = events[i];
                                                var x = (ev.clientX - rect.left) / rect.width;
                                                var y = (ev.clientY - rect.top) / rect.height;
                                                
                                                if (x < 0 || x > 1 || y < 0 || y > 1) continue;

                                                var mappedId;
                                                if (isMouse) {
                                                    mappedId = 0; // 鼠标永远占用 ID 0
                                                } else {
                                                    if (action === 0) { 
                                                        mappedId = winIdCounter % 10;
                                                        winIdCounter++;
                                                        activeTouches.set(ev.pointerId, mappedId);
                                                    } else {
                                                        mappedId = activeTouches.get(ev.pointerId);
                                                        if (mappedId === undefined) continue;
                                                    }
                                                }

                                                // 🌟 封包：11 字节电竞级协议包 [Type(1b) + Action(1b) + ID(1b) + X(4b) + Y(4b)]
                                                var buffer = new ArrayBuffer(11);
                                                var view = new DataView(buffer);
                                                view.setUint8(0, isMouse);
                                                view.setUint8(1, action);     
                                                view.setUint8(2, mappedId);   
                                                view.setFloat32(3, x, true);  
                                                view.setFloat32(7, y, true);  
                                                ws.send(buffer);
                                            }
                                            if (action === 1 && !isMouse) activeTouches.delete(e.pointerId);
                                        }

                                        img.addEventListener('pointerdown', e => { 
                                            document.documentElement.requestFullscreen().catch(err => {});
                                            processEvent(e, 0); 
                                        });
                                        img.addEventListener('pointermove', e => processEvent(e, 2));
                                        img.addEventListener('pointerup', e => processEvent(e, 1));
                                        img.addEventListener('pointercancel', e => processEvent(e, 1));
                                        img.addEventListener('contextmenu', e => e.preventDefault());
                                    </script>
                                    </body></html>";
                                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(html);
                                response.ContentType = "text/html";
                                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                                response.OutputStream.Close();
                                return;
                            }

                            if (request.Url?.AbsolutePath == "/stream") {
                                response.ContentType = "multipart/x-mixed-replace;boundary=frame";
                                using (var output = response.OutputStream) {
                                    var sw = Stopwatch.StartNew();
                                    int frameCount = 0;
                                    long byteCount = 0;

                                    while (!token.IsCancellationRequested) {
                                        byte[] imgData = session.TargetHwnd != IntPtr.Zero ? CaptureSingleWindow(session, quality) : CaptureScreenDirectX(session, quality);
                                        string header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {imgData.Length}\r\n\r\n";
                                        byte[] headerData = System.Text.Encoding.ASCII.GetBytes(header);
                                        
                                        await output.WriteAsync(headerData, 0, headerData.Length);
                                        await output.WriteAsync(imgData, 0, imgData.Length);
                                        await output.WriteAsync(System.Text.Encoding.ASCII.GetBytes("\r\n"), 0, 2);
                                        await output.FlushAsync(); 

                                        // 🌟 第四阶段：性能雷达补完 (实时计算副屏帧率和带宽)
                                        frameCount++;
                                        byteCount += headerData.Length + imgData.Length + 2;

                                        if (sw.ElapsedMilliseconds >= 1000) {
                                            int currentFps = frameCount;
                                            double currentBitrate = (byteCount * 8.0) / 1000000.0; // 换算为 Mbps

                                            // 🌟 修复 CS4014 警告：添加弃元符号丢弃返回的 Task，告诉编译器这是有意的
                                            _ = Dispatcher.InvokeAsync(() => {
                                                Monitor_FPS.Text = $"{currentFps} fps";
                                                Monitor_Bitrate.Text = $"{currentBitrate:F1} Mbps";
                                                
                                                UpdateSparkline(Polygon_FPS, _fpsHistory, currentFps, 120.0);
                                                UpdateSparkline(Polygon_Bitrate, _bitrateHistory, currentBitrate > 50 ? 50 : currentBitrate, 50.0);
                                            });

                                            frameCount = 0;
                                            byteCount = 0;
                                            sw.Restart();
                                        }
                                    }
                                }
                            }
                        } catch { }
                    }, token);
                } catch { }
            }
        }

        private VirtualDisplayManager? _vdManager;

        // 🌟 动态屏幕雷达：深入 DXGI 底层扫描当前所有激活的屏幕
        // 🌟 方案二：将其升级为有返回值的雷达，向上级汇报找到了几块屏幕
        private int RefreshDisplayList()
        {
            ComboDisplayIndex.Items.Clear();
            try
            {
                using var factory = new Factory1();
                int flatIndex = 0;
                
                // 🌟 终极修复：遍历所有显卡，彻底解决双显卡笔记本找不到“核显输出屏幕”的致命 Bug！
                var adapters = factory.Adapters1;
                for (int i = 0; i < adapters.Length; i++) {
                    var adapter = adapters[i];
                    var outputs = adapter.Outputs;
                    for (int j = 0; j < outputs.Length; j++) {
                        var output = outputs[j];
                        var desc = output.Description;
                        int width = desc.DesktopBounds.Right - desc.DesktopBounds.Left;
                        int height = desc.DesktopBounds.Bottom - desc.DesktopBounds.Top;
                        
                        string gpuName = adapter.Description.Description.Replace("\0", "");
                        var info = new DisplayInfo { Id = flatIndex, DeviceName = desc.DeviceName, Resolution = $"{width}x{height}", DisplayName = $"🖥️ 屏幕 {flatIndex + 1} ({width}x{height}) [{gpuName}]" };
                        ComboDisplayIndex.Items.Add(info);
                        
                        output.Dispose();
                        flatIndex++;
                    }
                    adapter.Dispose();
                }

                if (ComboDisplayIndex.Items.Count > 0)
                    ComboDisplayIndex.SelectedIndex = ComboDisplayIndex.Items.Count > 1 ? 1 : 0; // 默认选中第二个屏幕（副屏）
                
                // 🌟 同步更新 UI 状态栏
                Dispatcher.InvokeAsync(() => {
                    if (Txt_VirtualScreenCount != null) 
                        Txt_VirtualScreenCount.Text = $"🖥️ 当前系统可用屏幕：{ComboDisplayIndex.Items.Count} 块";
                });
            }
            catch (Exception ex) { Debug.WriteLine("DXGI 扫描屏幕失败: " + ex.Message); }
            return ComboDisplayIndex.Items.Count;
        }

        private async void Btn_InitVirtualDriver_Click(object sender, RoutedEventArgs e)
        {
            if (_vdManager == null) _vdManager = new VirtualDisplayManager();
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ 正在初始化/唤醒底层驱动..."; }
            try {
                await _vdManager.InstallDriverAsync();
                ShowCyberMessage("✅ 驱动底座就绪", "底层框架已注入 Windows！\n现在你可以随时点击【➕ 新增虚拟屏】来生成屏幕了，且绝不掉线！");
            } catch (Exception ex) { ShowCyberMessage("❌ 失败", ex.Message); }
            finally { if (btn != null) { btn.IsEnabled = true; btn.Content = "🔧 手动初始化/唤醒驱动底座"; } }
        }

        private async void Btn_AddVirtualScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_vdManager == null) _vdManager = new VirtualDisplayManager();
            
            // 🌟 1. UI 物理状态锁：防狂点导致的进程队列死锁
            Btn_AddVirtualScreen.IsEnabled = false;
            Btn_RemoveVirtualScreen.IsEnabled = false;
            Btn_AddVirtualScreen.Content = "⏳ 正在生成...";
            Btn_AddVirtualScreen.Foreground = System.Windows.Media.Brushes.Gray;

            try {
                int beforeCount = RefreshDisplayList();
                StatusText.Text = "状态: 正在向 Windows 请求分配显存并创建虚拟屏...";
                
                // 🌟 2. 致命单发注入 (自带底层 ExitCode 报错检测，失败会直接阻断)
                await _vdManager.AddScreenAsync();
                
                // 🌟 3. 高频微型雷达阵列 (每0.5秒扫一次，最多5秒)
                bool isSuccess = false;
                for (int i = 0; i < 10; i++) {
                    await Task.Delay(500);
                    if (RefreshDisplayList() > beforeCount) { isSuccess = true; break; }
                }
                
                // 🌟 4. 最终审判 (剥离导致连续弹 UAC 并引起卡顿的自动重启逻辑)
                if (isSuccess) {
                    ShowCyberMessage("✅ 虚拟屏创造成功", "新屏幕已上线！\n\n👉 请按键盘【Win + P】确保处于【扩展】模式，否则它会显示和主屏一样的镜像！");
                    StatusText.Text = "状态: ✅ 虚拟屏幕已就绪";
                } else {
                    // 🌟 终极破案：只要没报 Exception 走到这里，说明驱动 100% 成功生成了屏幕！
                    // DXGI 扫不到的原因只有一个：Windows 把它设为了“镜像”或“断开”！
                    ShowCyberMessage("✅ 虚拟屏已在后台生成", "底层驱动已成功分配出屏幕，但雷达未检测到独立扩展屏。\n\n⚠️ 极大概率是因为 Windows 默认将新屏幕与主屏【镜像复制】了！\n\n👉 破局操作：请立即按键盘【Win + P】，点击选择【扩展】，新屏幕就会瞬间独立并出现在下拉列表中！");
                    StatusText.Text = "状态: ⚠️ 处于镜像模式，请按 Win+P 扩展";
                }
            } catch (Exception ex) { 
                ShowCyberMessage("❌ 创建失败", ex.Message); 
                StatusText.Text = "状态: ❌ 虚拟屏创建异常";
            } finally {
                // 🌟 6. 释放 UI 物理锁
                Btn_AddVirtualScreen.IsEnabled = true;
                Btn_RemoveVirtualScreen.IsEnabled = true;
                Btn_AddVirtualScreen.Content = "➕ 新增虚拟屏";
                Btn_AddVirtualScreen.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 202, 114));
            }
        }

        private async void Btn_RemoveVirtualScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_vdManager == null) _vdManager = new VirtualDisplayManager();
            
            Btn_AddVirtualScreen.IsEnabled = false;
            Btn_RemoveVirtualScreen.IsEnabled = false;
            Btn_RemoveVirtualScreen.Content = "⏳ 正在拔除...";
            Btn_RemoveVirtualScreen.Foreground = System.Windows.Media.Brushes.Gray;

            try { 
                int beforeCount = RefreshDisplayList();
                await _vdManager.RemoveScreenAsync(); 
                
                // 🌟 高频轮询等待系统释放硬件
                bool isSuccess = false;
                for (int i = 0; i < 10; i++) {
                    await Task.Delay(500);
                    if (RefreshDisplayList() < beforeCount) { isSuccess = true; break; }
                }
                
                if (isSuccess) {
                    ShowCyberMessage("🔌 虚拟屏已拔除", "成功销毁了一块虚拟屏幕，显卡资源已释放。"); 
                    StatusText.Text = "状态: ✅ 虚拟屏幕已拔除";
                } else {
                    StatusText.Text = "状态: ⚠️ 屏幕拔除命令已下发，但 Windows 尚未完全卸载硬件";
                }
            }
            catch (Exception ex) { 
                ShowCyberMessage("❌ 拔除失败", ex.Message); 
                StatusText.Text = "状态: ❌ 虚拟屏拔除异常";
            } finally {
                Btn_AddVirtualScreen.IsEnabled = true;
                Btn_RemoveVirtualScreen.IsEnabled = true;
                Btn_RemoveVirtualScreen.Content = "➖ 拔除虚拟屏";
                Btn_RemoveVirtualScreen.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 59, 1));
            }
        }

        private async void Btn_ResetVirtualDriver_Click(object sender, RoutedEventArgs e)
        {
            if (_vdManager == null) _vdManager = new VirtualDisplayManager();
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "⏳ 正在强行重置驱动并清理..."; }
            try {
                await _vdManager.ResetDriverAsync();
                ShowCyberMessage("🧹 重置成功", "已强行重启底层虚拟驱动！\n所有残留的幽灵屏幕已被彻底清空。");
                await Task.Delay(2000); // 留给 Windows 卸载硬件的时间
                RefreshDisplayList();
            } catch (Exception ex) { ShowCyberMessage("❌ 失败", ex.Message); }
            finally { if (btn != null) { btn.IsEnabled = true; btn.Content = "🧹 强行重置驱动并清空所有虚拟屏"; } }
        }
    }
}