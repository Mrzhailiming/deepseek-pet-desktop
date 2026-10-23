using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeepseekPet.Platform
{
    /// <summary>
    /// 托盘图标：左键 / 右键都弹出一个系统菜单（显示 / 隐藏、面板、退出 …，菜单项由调用方给）。
    /// 桌宠藏起来了、被全屏窗口盖住了，都能从这里找回来。
    ///
    /// 实现：给桌宠窗口挂一个子类化的窗口过程收托盘回调；回调里只把点击排进队列，菜单在 <see cref="Pump"/>（主线程的
    /// Update）里弹 —— 别在窗口过程里碰 Unity 的东西。资源管理器重启（TaskbarCreated）时自动重新加图标。
    /// 只在 Windows 播放器里生效，其余情况全是空操作。
    /// </summary>
    public static class TrayIcon
    {
        public sealed class Item
        {
            public Func<string> Label;
            public Action Run;
            public Func<bool> Checked;
        }

        static readonly ConcurrentQueue<int> Clicks = new ConcurrentQueue<int>();
        static readonly List<Item> Items = new List<Item>();
        static bool _added;

        public static bool Added => _added;

        /// <summary>加图标（DesktopWindow 先 Init）。tip 是悬停提示。</summary>
        public static void Add(string tip, IEnumerable<Item> items)
        {
            Items.Clear();
            Items.AddRange(items);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!DesktopWindow.Ready || _added) return;
            _hwnd = DesktopWindow.Handle;
            _tip = tip;
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            _proc = WndProc;
            _oldProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_proc));
            _icon = LoadOwnIcon();
            _added = Notify(NIM_ADD);
#endif
        }

        public static void Remove()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!_added) return;
            Notify(NIM_DELETE);
            if (_oldProc != IntPtr.Zero) SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldProc);
            _oldProc = IntPtr.Zero;
            _added = false;
#endif
        }

        /// <summary>主线程上调（每帧）：有托盘点击就弹菜单、执行选中的那一项。</summary>
        public static void Pump()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!Clicks.TryDequeue(out _)) return;
            while (Clicks.TryDequeue(out _)) { }
            var menu = CreatePopupMenu();
            for (var i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                var flags = MF_STRING | (item.Checked != null && item.Checked() ? MF_CHECKED : 0);
                AppendMenu(menu, flags, (UIntPtr)(i + 1), item.Label());
            }
            GetCursorPos(out var p);
            // 不先抢前台的话，点菜单外面菜单不会自己收起来（TrackPopupMenu 的老规矩）。
            SetForegroundWindow(_hwnd);
            var chosen = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
            DestroyMenu(menu);
            if (chosen > 0 && chosen <= Items.Count) Items[chosen - 1].Run();
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const int GWLP_WNDPROC = -4;
        const uint WM_APP = 0x8000;
        const uint TrayMessage = WM_APP + 0x47;
        const uint WM_LBUTTONUP = 0x0202;
        const uint WM_RBUTTONUP = 0x0205;
        const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
        const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
        const uint MF_STRING = 0, MF_CHECKED = 8;
        const uint TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100;

        delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        static IntPtr _hwnd;
        static string _tip;
        static uint _taskbarCreated;
        static WndProcDelegate _proc; // 存住：被 GC 收掉的话，窗口过程会跳进野指针。
        static IntPtr _oldProc;
        static IntPtr _icon;

        static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == TrayMessage)
            {
                var e = (uint)(lParam.ToInt64() & 0xFFFF);
                if (e == WM_LBUTTONUP || e == WM_RBUTTONUP) Clicks.Enqueue((int)e);
                return IntPtr.Zero;
            }
            if (msg == _taskbarCreated && _taskbarCreated != 0) Notify(NIM_ADD);
            return CallWindowProc(_oldProc, hwnd, msg, wParam, lParam);
        }

        static IntPtr LoadOwnIcon()
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            var small = new IntPtr[1];
            if (exe != null && ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero) return small[0];
            return LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
        }

        static bool Notify(uint action)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = TrayMessage,
                hIcon = _icon,
                szTip = _tip ?? "",
                szInfo = "",
                szInfoTitle = "",
            };
            return Shell_NotifyIcon(action, ref data);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X, Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")] static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] static extern IntPtr CallWindowProc(IntPtr proc, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
        [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
#endif
    }
}
