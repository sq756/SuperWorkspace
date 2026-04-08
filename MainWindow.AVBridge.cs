using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // ==========================================
        // 模块 6：虚拟视听桥接 (AV Bridge)
        // ==========================================
        
        private MemoryMappedFile? _virtualCamMmf;
        private MemoryMappedViewAccessor? _virtualCamAccessor;
        private int _camWidth = 1920;
        private int _camHeight = 1080;
        private CancellationTokenSource? _virtualCamCts;
        private uint _frameIndex = 0; // 🌟 1. 声明帧序号变量

        // 🌟 Native API 声明
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }

        // 🌟 初始化共享内存管道（对接 UnityCapture 或同类虚拟驱动）
        private void InitializeVirtualCamera(int width, int height)
        {
            try
            {
                _camWidth = width;
                _camHeight = height;
                
                // 报头占用 16 字节 [Width(4) + Height(4) + Format(4) + FrameIndex(4)]
                const int headerSize = 16;
                int pixelSize = width * height * 4; 
                int totalSize = headerSize + pixelSize;
                
                // 🌟 清理旧管道，防止手机横竖屏切换时发生内存访问冲突
                _virtualCamAccessor?.Dispose();
                _virtualCamMmf?.Dispose();

                // 🌟 修正：UnityCapture 默认寻找的是 "UnityCapture1"
                _virtualCamMmf = MemoryMappedFile.CreateOrOpen("UnityCapture1", totalSize);
                _virtualCamAccessor = _virtualCamMmf.CreateViewAccessor();

                // 初始化报头信息
                _virtualCamAccessor.Write(0, width);   // Offset 0: Width
                _virtualCamAccessor.Write(4, height);  // Offset 4: Height
                _virtualCamAccessor.Write(8, 0);       // Offset 8: Format (0 代表 ARGB32)
            }
            catch (Exception ex)
            {
                ShowCyberMessage("❌ 初始化失败", "虚拟摄像头管道初始化失败:\n" + ex.Message);
            }
        }

        // 🌟 核心引擎：将图像直接写入系统级虚拟摄像头 (跳过所有中间件)
        private void PushFrameToSystemCamera(Bitmap bitmap)
        {
            if (_virtualCamAccessor == null) return;

            // 锁定 Bitmap 内存，阻止 C# 的垃圾回收器移动它
            BitmapData bmpData = bitmap.LockBits(
                new Rectangle(0, 0, Math.Min(bitmap.Width, _camWidth), Math.Min(bitmap.Height, _camHeight)), 
                ImageLockMode.ReadOnly, 
                PixelFormat.Format32bppArgb
            );

            try
            {
                // 🌟 核心：跳过前 16 字节报头，从 Offset 16 开始写入像素
                IntPtr basePtr = _virtualCamAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
                IntPtr pixelDestPtr = IntPtr.Add(basePtr, 16); 

                SharpDX.Utilities.CopyMemory(pixelDestPtr, bmpData.Scan0, bmpData.Stride * bmpData.Height);

                // 🌟 极其重要：更新并写入帧序号，驱动看到序号增加才会刷新画面
                _frameIndex++;
                _virtualCamAccessor.Write(12, _frameIndex); // Offset 12: FrameIndex
            }
            finally { bitmap.UnlockBits(bmpData); }
        }

        // 🌟 隐藏式后台截屏循环
        public void StartVirtualCameraLoop()
        {
            _virtualCamCts = new CancellationTokenSource();
            var token = _virtualCamCts.Token;

            Task.Run(() => 
            {
                Bitmap? windowBmp = null;
                Graphics? gWindow = null;
                int lastWidth = 0;
                int lastHeight = 0;

                while (!token.IsCancellationRequested)
                {
                    try 
                    {
                        IntPtr hwnd = FindWindow(null, "SuperWorkspaceCamera"); // 🌟 修复 2：同步窗口名称
                        if (hwnd != IntPtr.Zero)
                        {
                            GetClientRect(hwnd, out RECT rect);
                            int width = rect.right - rect.left;
                            int height = rect.bottom - rect.top;

                            if (width > 0 && height > 0)
                            {
                                // 🌟 动态适配分辨率：手机横竖屏切换时自动重新初始化管道
                                if (width != lastWidth || height != lastHeight)
                                {
                                    lastWidth = width;
                                    lastHeight = height;
                                    
                                    InitializeVirtualCamera(width, height);
                                    
                                    gWindow?.Dispose();
                                    windowBmp?.Dispose();
                                    
                                    // 准备可复用的画布，彻底消灭每秒 30 次的内存分配垃圾
                                    windowBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                                    gWindow = Graphics.FromImage(windowBmp);
                                }

                                if (windowBmp != null && gWindow != null)
                                {
                                    IntPtr hdcDest = gWindow.GetHdc();
                                    // 🌟 核心魔法：参数 3 (PW_CLIENTONLY | PW_RENDERFULLCONTENT) 
                                    // 专门用于强制抓取被遮挡、最小化或扔在屏幕外的 DWM 硬件加速窗口！
                                    bool success = PrintWindow(hwnd, hdcDest, 3); 
                                    gWindow.ReleaseHdc(hdcDest);

                                    // 🌟 确保画面抓取成功了，才往系统驱动里灌数据
                                    if (success) 
                                        PushFrameToSystemCamera(windowBmp);
                                }
                            }
                        }
                    } 
                    catch { /* 忽略抓取时的偶发冲突 */ }

                    Thread.Sleep(33); // 锁定约 30 FPS
                }

                gWindow?.Dispose();
                windowBmp?.Dispose();
            }, token);
        }

        public void StopVirtualCameraLoop()
        {
            _virtualCamCts?.Cancel();
        }

        // 🌟 自动化部署：一键将虚拟摄像头驱动写入 Windows 内核
        public void InstallVirtualCameraDriver()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string? dllPath64 = null;
            string? dllPath32 = null;

            // 🌟 核心修复：停止扫描整个桌面防卡死！精准扫描当前目录、项目根目录和 scrcpy 目录
            string prjRoot = "";
            try { prjRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..")); } catch {}
            
            var searchDirs = new System.Collections.Generic.List<string> { baseDir };
            if (Directory.Exists(prjRoot)) searchDirs.Add(prjRoot);
            searchDirs.Add(@"C:\Users\S\scrcpy");

            foreach (var dir in searchDirs)
            {
                if (Directory.Exists(dir))
                {
                    var files64 = Directory.GetFiles(dir, "UnityCaptureFilter64bit.dll", SearchOption.AllDirectories);
                    var files32 = Directory.GetFiles(dir, "UnityCaptureFilter32bit.dll", SearchOption.AllDirectories);
                    if (files64.Length > 0) dllPath64 = files64[0];
                    if (files32.Length > 0) dllPath32 = files32[0];
                    if (dllPath64 != null) break;
                }
            }

            if (dllPath64 == null)
            {
                ShowCyberMessage("⚠️ 文件缺失", "找不到驱动核心文件！\n请确保将 'UnityCaptureFilter64bit.dll' 和 'UnityCaptureFilter32bit.dll' 放到了软件同目录下，或者之前解压的 scrcpy 目录中。");
                return;
            }

            try
            {
                // 🌟 核心魔法：使用 runas 参数，优雅拉起系统的管理员授权弹窗进行静默注册
                if (dllPath32 != null) Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "regsvr32.exe", Arguments = $"/s \"{dllPath32}\"", Verb = "runas", UseShellExecute = true })?.WaitForExit();
                Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "regsvr32.exe", Arguments = $"/s \"{dllPath64}\"", Verb = "runas", UseShellExecute = true })?.WaitForExit();
                
                ShowCyberMessage("✅ 部署成功", "虚拟摄像头底层驱动已成功注册到系统！\n\n现在打开微信、腾讯会议或相机，就能直接看到 [Unity Video Capture] 设备了。");
            }
            catch (Exception ex) { ShowCyberMessage("❌ 权限不足", "驱动注册取消或失败:\n" + ex.Message); }
        }
    }
}