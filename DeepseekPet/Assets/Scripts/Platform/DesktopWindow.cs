using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DeepseekPet.Platform
{
    /// <summary>屏幕坐标里的一个矩形（像素，原点在主屏左上角，y 向下）。</summary>
    public struct ScreenRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public ScreenRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public override string ToString()
        {
            return "(" + Left + "," + Top + " " + Width + "×" + Height + ")";
        }
    }

    /// <summary>
    /// 桌宠窗口：无边框、背景透明、置顶、不占任务栏，鼠标不在宠物身上时整窗点击穿透。
    ///
    /// **全项目只有这里调 Win32**。只在 Windows 播放器里生效；编辑器里（和别的平台）<see cref="IsSupported"/> 为 false，
    /// 所有方法都是空操作 —— 别把 Unity 编辑器自己的窗口改成透明的。
    ///
    /// 透明靠 DwmExtendFrameIntoClientArea + 相机清成 (0,0,0,0)，要求 Player 设置里关掉 DXGI flip model
    /// （PetBuild 打包时会设）。点击穿透靠切换 WS_EX_TRANSPARENT：分层窗口（WS_EX_LAYERED，整体不透明度 255）
    /// 加上它，鼠标消息就落到下面的窗口去了。
    /// </summary>
    public static class DesktopWindow
    {
        public static bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        static IntPtr _hwnd;
        static bool _clickThrough;
        static bool _noActivate = true;
        static bool _ready;

        /// <summary>初始化过了（找到了窗口并改好了样式）。</summary>
        public static bool Ready => _ready;
        public static bool ClickThrough => _clickThrough;

        /// <summary>窗口句柄（托盘图标要挂在它上面收消息）。</summary>
        public static IntPtr Handle => _hwnd;

        static bool _visible = true;
        public static bool Visible => _visible;

        /// <summary>藏起 / 露出整个窗口（托盘菜单用）。藏起来时程序照跑，事件照喂。</summary>
        public static void SetVisible(bool on)
        {
            if (!_ready || on == _visible) return;
            _visible = on;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            ShowWindow(_hwnd, on ? SW_SHOWNOACTIVATE : SW_HIDE);
            if (on) KeepOnTop();
#endif
        }

        /// <summary>把当前播放器窗口变成桌宠窗口：无边框、透明、置顶、不占任务栏，放在 (x, y)，大小 width × height（像素）。</summary>
        public static bool Init(int x, int y, int width, int height)
        {
            if (!IsSupported) return false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _hwnd = FindOwnWindow();
            if (_hwnd == IntPtr.Zero) return false;
            SetWindowLongPtr(_hwnd, GWL_STYLE, (IntPtr)(WS_POPUP | WS_VISIBLE));
            _clickThrough = false;
            ApplyExStyle();
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
            _ready = true;
            return true;
#else
            return false;
#endif
        }

        /// <summary>整窗点击穿透（鼠标落到下面的窗口）开 / 关。</summary>
        public static void SetClickThrough(bool on)
        {
            if (!_ready || on == _clickThrough) return;
            _clickThrough = on;
            ApplyExStyle();
        }

        /// <summary>
        /// 点它的时候不抢焦点（WS_EX_NOACTIVATE，默认开）：摸一下宠物，正在打字的编辑器不该失去焦点。
        /// 代价是收不到键盘，所以调试面板打开时关掉。
        /// </summary>
        public static void SetNoActivate(bool on)
        {
            if (on == _noActivate) return;
            _noActivate = on;
            if (_ready) ApplyExStyle();
        }

        static void ApplyExStyle()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var ex = WS_EX_LAYERED | WS_EX_TOOLWINDOW | (_clickThrough ? WS_EX_TRANSPARENT : 0) | (_noActivate ? WS_EX_NOACTIVATE : 0);
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (IntPtr)ex);
#endif
        }

        /// <summary>挪窗口（左上角到屏幕坐标 x, y）。</summary>
        public static void Move(int x, int y)
        {
            if (!_ready) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
#endif
        }

        /// <summary>同时改位置和大小。</summary>
        public static void SetBounds(int x, int y, int width, int height)
        {
            if (!_ready) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE);
#endif
        }

        /// <summary>重新压到最上层：别的置顶窗口（任务管理器、全屏视频）会把它盖住，定期调一下。</summary>
        public static void KeepOnTop()
        {
            if (!_ready) return;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
#endif
        }

        /// <summary>窗口在屏幕上的位置。</summary>
        public static ScreenRect WindowRect
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (_ready && GetWindowRect(_hwnd, out var r)) return new ScreenRect(r.Left, r.Top, r.Right, r.Bottom);
#endif
                return new ScreenRect(0, 0, Screen.width, Screen.height);
            }
        }

        /// <summary>光标的屏幕坐标（点击穿透时 Unity 自己收不到鼠标，只能这样问）。</summary>
        public static Vector2Int CursorScreen
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (GetCursorPos(out var p)) return new Vector2Int(p.X, p.Y);
#endif
                var m = Input.mousePosition;
                return new Vector2Int((int)m.x, Screen.height - (int)m.y);
            }
        }

        /// <summary>光标换算到 Unity 的屏幕坐标（窗口客户区，原点左下，y 向上）。</summary>
        public static Vector2 CursorInWindow
        {
            get
            {
                if (!_ready) return Input.mousePosition;
                var c = CursorScreen;
                var w = WindowRect;
                return new Vector2(c.x - w.Left, w.Bottom - c.y);
            }
        }

        /// <summary>鼠标左 / 右键此刻是否按着（点击穿透时也问得到）。</summary>
        public static bool MouseDown(int button)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var vk = button == 0 ? VK_LBUTTON : button == 1 ? VK_RBUTTON : VK_MBUTTON;
            // 左右键对调的系统设置下，VK_LBUTTON 报的是物理左键 —— 这里要的是逻辑主键。
            if (button < 2 && GetSystemMetrics(SM_SWAPBUTTON) != 0) vk = button == 0 ? VK_RBUTTON : VK_LBUTTON;
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
#else
            return Input.GetMouseButton(button);
#endif
        }

        /// <summary>这个点所在显示器的工作区（去掉任务栏）。</summary>
        public static ScreenRect WorkAreaAt(int x, int y)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                return new ScreenRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            }
#endif
            return new ScreenRect(0, 0, Display.main.systemWidth, Display.main.systemHeight);
        }

        /// <summary>
        /// 诊断：环境变量 DEEPSEEK_PET_SCALE（比如 1.5）强行指定缩放，在 100% 的屏上检查 150% 下的排版。
        /// 只替换「系统报的 DPI」这一步，之后的窗口大小、界面缩放、命中判定都照常走。
        /// </summary>
        static readonly float ScaleOverride = ParseScale(Environment.GetEnvironmentVariable("DEEPSEEK_PET_SCALE"));

        static float ParseScale(string text)
        {
            return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0.5f && v <= 4f ? v : 0f;
        }

        /// <summary>窗口所在显示器的缩放（96 dpi = 1）。</summary>
        public static float Scale
        {
            get
            {
                if (ScaleOverride > 0) return ScaleOverride;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (_ready)
                {
                    try
                    {
                        var dpi = GetDpiForWindow(_hwnd);
                        if (dpi > 0) return dpi / 96f;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // Windows 10 1607 之前没有 GetDpiForWindow。
                    }
                }
#endif
                return Screen.dpi > 0 ? Screen.dpi / 96f : 1f;
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        static IntPtr FindOwnWindow()
        {
            var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var found = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var owner);
                if (owner != pid || !IsWindowVisible(hwnd)) return true;
                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() != "UnityWndClass") return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found != IntPtr.Zero ? found : GetActiveWindow();
        }

        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const uint WS_POPUP = 0x80000000;
        const uint WS_VISIBLE = 0x10000000;
        const uint WS_EX_LAYERED = 0x00080000;
        const uint WS_EX_TRANSPARENT = 0x00000020;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint WS_EX_NOACTIVATE = 0x08000000;
        const uint LWA_ALPHA = 0x2;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        const int VK_LBUTTON = 0x01;
        const int VK_RBUTTON = 0x02;
        const int VK_MBUTTON = 0x04;
        const int SM_SWAPBUTTON = 23;
        const int SW_HIDE = 0;
        const int SW_SHOWNOACTIVATE = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
#endif
    }
}
