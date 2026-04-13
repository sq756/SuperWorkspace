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
        
        private IntPtr _virtualCamHandle = IntPtr.Zero;
        private IntPtr _virtualCamPtr = IntPtr.Zero;
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

        // 🌟 破除沙盒与幽灵化必需的底层 API
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr lpFileMappingAttributes, uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string lpName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string StringSecurityDescriptor, uint StringSDRevision, out IntPtr SecurityDescriptor, out uint SecurityDescriptorSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr hMem);
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [StructLayout(LayoutKind.Sequential)] private struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public int bInheritHandle; }
        private const int GWL_EXSTYLE = -20; private const int WS_EX_LAYERED = 0x00080000; private const int WS_EX_TOOLWINDOW = 0x00000080; private const int WS_EX_TRANSPARENT = 0x00000020; private const uint LWA_ALPHA = 0x00000002;

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
                if (_virtualCamPtr != IntPtr.Zero) { UnmapViewOfFile(_virtualCamPtr); _virtualCamPtr = IntPtr.Zero; }
                if (_virtualCamHandle != IntPtr.Zero) { CloseHandle(_virtualCamHandle); _virtualCamHandle = IntPtr.Zero; }

                // 🌟 核心突破：注入 SDDL 魔法，击穿浏览器沙盒（允许最低权限读取数据）
                string sddl = "D:(A;;GA;;;WD)(A;;GA;;;AN)S:(ML;;NW;;;LW)";
                ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out IntPtr pSecurityDescriptor, out uint sdSize);
                SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), lpSecurityDescriptor = pSecurityDescriptor, bInheritHandle = 0 };
                IntPtr pSa = Marshal.AllocHGlobal(sa.nLength); Marshal.StructureToPtr(sa, pSa, false);

                _virtualCamHandle = CreateFileMapping(new IntPtr(-1), pSa, 0x04, 0, (uint)totalSize, @"Global\UnityCapture1"); // 0x04 = PAGE_READWRITE
                Marshal.FreeHGlobal(pSa); LocalFree(pSecurityDescriptor);

                if (_virtualCamHandle != IntPtr.Zero)
                {
                    _virtualCamPtr = MapViewOfFile(_virtualCamHandle, 0xF001F, 0, 0, UIntPtr.Zero); // 0xF001F = FILE_MAP_ALL_ACCESS
                    if (_virtualCamPtr != IntPtr.Zero) {
                        Marshal.WriteInt32(_virtualCamPtr, 0, width);
                        Marshal.WriteInt32(_virtualCamPtr, 4, height);
                        Marshal.WriteInt32(_virtualCamPtr, 8, 0); // Format 0 = ARGB32
                    }
                }
            }
            catch (Exception ex)
            {
                ShowCyberMessage("❌ 初始化失败", "虚拟摄像头管道初始化失败:\n" + ex.Message);
            }
        }

        // 🌟 核心引擎：将图像直接写入系统级虚拟摄像头 (跳过所有中间件)
        private void PushFrameToSystemCamera(Bitmap bitmap)
        {
            if (_virtualCamPtr == IntPtr.Zero) return;

            // 锁定 Bitmap 内存，阻止 C# 的垃圾回收器移动它
            BitmapData bmpData = bitmap.LockBits(
                new Rectangle(0, 0, Math.Min(bitmap.Width, _camWidth), Math.Min(bitmap.Height, _camHeight)), 
                ImageLockMode.ReadOnly, 
                PixelFormat.Format32bppArgb
            );

            try
            {
                // 🌟 核心：跳过前 16 字节报头，从 Offset 16 开始写入像素
                IntPtr pixelDestPtr = IntPtr.Add(_virtualCamPtr, 16); 

                SharpDX.Utilities.CopyMemory(pixelDestPtr, bmpData.Scan0, bmpData.Stride * bmpData.Height);

                // 🌟 极其重要：更新并写入帧序号，驱动看到序号增加才会刷新画面
                _frameIndex++;
                Marshal.WriteInt32(_virtualCamPtr, 12, (int)_frameIndex); // Offset 12: FrameIndex
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
                            // 🌟 终极隐身术：一旦发现窗口，立刻赋予幽灵属性，从桌面和任务栏抹除痕迹！
                            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                            if ((style & WS_EX_LAYERED) == 0) {
                                SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
                                SetLayeredWindowAttributes(hwnd, 0, 1, LWA_ALPHA); // 1/255 透明度，肉眼绝对看不见！
                            }

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
                                    {
                                        PushFrameToSystemCamera(windowBmp);
                                        OnVideoFrameCaptured?.Invoke(windowBmp); // 🌟 触发扩展生态的视频流 Hook (供 YOLO 等插件使用)
                                    }
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
            if (_virtualCamPtr != IntPtr.Zero) { UnmapViewOfFile(_virtualCamPtr); _virtualCamPtr = IntPtr.Zero; }
            if (_virtualCamHandle != IntPtr.Zero) { CloseHandle(_virtualCamHandle); _virtualCamHandle = IntPtr.Zero; }
        }

        // ==========================================
        // 🌟 二进制手术刀：在 C# 层直接黑入 C++ DLL 篡改名字！
        // ==========================================
        private string PatchVirtualCameraDll(string sourceDllPath)
        {
            try {
                string destDllPath = Path.Combine(Path.GetDirectoryName(sourceDllPath) ?? "", "SuperWorkspaceCam" + (sourceDllPath.Contains("64") ? "64" : "32") + ".dll");
                byte[] dllBytes = File.ReadAllBytes(sourceDllPath);
                
                // 🌟 核心魔法：19个字符换19个字符，完美吻合内存偏移，绝不破坏 C++ DLL 结构！
                byte[] oldAscii = System.Text.Encoding.ASCII.GetBytes("Unity Video Capture");
                byte[] newAscii = System.Text.Encoding.ASCII.GetBytes("SuperWorkspace Cam ");
                byte[] oldUnicode = System.Text.Encoding.Unicode.GetBytes("Unity Video Capture");
                byte[] newUnicode = System.Text.Encoding.Unicode.GetBytes("SuperWorkspace Cam ");
                
                ReplaceDllBytes(dllBytes, oldAscii, newAscii);
                ReplaceDllBytes(dllBytes, oldUnicode, newUnicode);
                
                File.WriteAllBytes(destDllPath, dllBytes);
                return destDllPath;
            } catch { return sourceDllPath; } // 失败则退回原版
        }

        private void ReplaceDllBytes(byte[] source, byte[] search, byte[] replace)
        {
            for (int i = 0; i <= source.Length - search.Length; i++) {
                bool match = true;
                for (int j = 0; j < search.Length; j++) { if (source[i + j] != search[j]) { match = false; break; } }
                if (match) for (int j = 0; j < replace.Length; j++) source[i + j] = replace[j];
            }
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

            // 🌟 部署前先做二进制开颅手术！
            string patchedDll64 = PatchVirtualCameraDll(dllPath64);
            string patchedDll32 = dllPath32 != null ? PatchVirtualCameraDll(dllPath32) : "";

            try
            {
                // 🌟 体验优化：通过写入批处理，将可能产生的多次 UAC 授权弹窗合并为 1 次！
                string batPath = Path.Combine(Path.GetTempPath(), "SuperWorkspace_CamDeploy.bat");
                string batCmds = "@echo off\r\n";
                if (!string.IsNullOrEmpty(patchedDll32)) {
                    batCmds += $"regsvr32.exe /u /s \"{patchedDll32}\"\r\nregsvr32.exe \"{patchedDll32}\"\r\n";
                    batCmds += "if %errorlevel% neq 0 exit /b %errorlevel%\r\n"; // 🌟 一旦失败，立刻带着错误码暴毙退出
                }
                batCmds += $"regsvr32.exe /u /s \"{patchedDll64}\"\r\nregsvr32.exe \"{patchedDll64}\"";
                batCmds += "\r\nif %errorlevel% neq 0 exit /b %errorlevel%\r\n";
                File.WriteAllText(batPath, batCmds, System.Text.Encoding.Default);

                ProcessStartInfo psi = new ProcessStartInfo { FileName = batPath, UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true };
                using (Process? process = Process.Start(psi))
                {
                    process?.WaitForExit();
                    // 🌟 严苛校验：只有 ExitCode 是 0 才是真成功！
                    if (process != null && process.ExitCode != 0)
                    {
                        ShowCyberMessage("❌ 部署失败", $"虚拟摄像头注入中断！错误码: {process.ExitCode}\n\n这通常是因为您的系统缺少微软 VC++ 运行库。\n请根据刚才系统的弹窗报错进行修复后再试。");
                        return;
                    }
                }
                try { if (File.Exists(batPath)) File.Delete(batPath); } catch {}
                
                ShowCyberMessage("✅ 部署成功", "虚拟摄像头已成功注入 Windows 内核！\n\n现在打开微信或浏览器，就能直接使用专属的 [SuperWorkspace Cam] 设备了！");
            }
            catch (Exception ex) { ShowCyberMessage("❌ 权限不足", "驱动注册取消或失败:\n" + ex.Message); }
        }
    }
}