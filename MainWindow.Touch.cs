using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows; // 🌟 修复 SystemParameters 报错

namespace SuperWorkspace
{
    public partial class MainWindow
    {
        // --- 鼠标反向控制核心 API ---
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);
        
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        private const uint MOUSEEVENTF_LEFTUP = 0x04;

        // 🌟 鼠标单点降级专用：记录当前哪根手指拥有鼠标控制权（255表示空闲）
        private byte _mouseOwnerId = 255;

        private void InjectTouchFromNetwork(byte type, byte action, byte id, float x, float y, DisplaySession? session = null)
        {
            int targetX = 0, targetY = 0;
            
            if (session != null && session.CaptureWidth > 0 && session.CaptureHeight > 0) {
                targetX = session.OffsetX + (int)(x * session.CaptureWidth);
                targetY = session.OffsetY + (int)(y * session.CaptureHeight);
            } else {
                targetX = (int)(x * SystemParameters.PrimaryScreenWidth);
                targetY = (int)(y * SystemParameters.PrimaryScreenHeight);
            }

            // 🌟 临时降维打击：为了绝对的稳定与流畅，全部强制转为底层单点鼠标事件！
            // (留坑：我们的 11 字节协议和 120Hz 采集引擎已就绪，等后续接入 VMulti 驱动后再恢复多点触控逻辑)
            if (action == 0 && _mouseOwnerId == 255) 
            {
                _mouseOwnerId = id; // 第一根按下的手指获取控制权
                
                // 🌟 霸道抢夺焦点：如果投射的是特定单窗口，点击前必须先把它拽到前台！
                // 否则鼠标的点击会结结实实地打在遮挡它的其他软件上！
                if (session != null && session.TargetHwnd != IntPtr.Zero) {
                    SetForegroundWindow(session.TargetHwnd);
                }
                
                SetCursorPos(targetX, targetY);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0); 
            }
            else if (id == _mouseOwnerId)
            {
                SetCursorPos(targetX, targetY); // 拥有控制权的手指移动
                if (action == 1) 
                {
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0); 
                    _mouseOwnerId = 255; // 抬起后释放控制权
                }
            }
        }
    }

    public static class TouchInjector
    {
        [DllImport("User32.dll")]
        public static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

        [DllImport("User32.dll")]
        public static extern bool InjectTouchInput(uint count, [MarshalAs(UnmanagedType.LPArray)] POINTER_TOUCH_INFO[] contacts);

        public const uint MAX_TOUCH_COUNT = 10;
        public const uint TOUCH_FEEDBACK_NONE = 0x00000002;

        public const uint PT_TOUCH = 0x00000002;
        public const uint POINTER_FLAG_NEW = 0x00000001;
        public const uint POINTER_FLAG_INRANGE = 0x00000002;
        public const uint POINTER_FLAG_INCONTACT = 0x00000004;
        public const uint POINTER_FLAG_DOWN = 0x00010000;
        public const uint POINTER_FLAG_UPDATE = 0x00020000;
        public const uint POINTER_FLAG_UP = 0x00040000;
        public const uint POINTER_FLAG_CANCELED = 0x00000800;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_TOUCH_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint touchFlags;
            public uint touchMask;
            public RECT rcContact;
            public RECT rcContactRaw;
            public uint orientation;
            public uint pressure;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINTER_INFO
        {
            public uint pointerType;
            public uint pointerId;
            public uint frameId;
            public uint pointerFlags;
            public IntPtr sourceDevice;
            public IntPtr hwndTarget;
            public POINT ptPixelLocation;
            public POINT ptHimetricLocation;
            public POINT ptPixelLocationRaw;
            public POINT ptHimetricLocationRaw;
            public uint dwTime;
            public uint historyCount;
            public int InputData;
            public uint dwKeyStates;
            public ulong PerformanceCount;
            public int ButtonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left; public int top; public int right; public int bottom; }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }
    }
}