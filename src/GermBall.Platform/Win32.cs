using System;
using System.Runtime.InteropServices;

namespace GermBall.Win;

/// <summary>The few Win32 calls the game needs.</summary>
public static unsafe class Win32
{
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000, WS_POPUP = 0x80000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_CLOSE = 0x0010, WM_QUIT = 0x0012,
        WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_INPUT = 0x00FF,
        WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205,
        WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A, WM_MOUSEMOVE = 0x0200, WM_CHAR = 0x0102, WM_LBUTTONDBLCLK = 0x0203, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    public const uint PM_REMOVE = 1;
    public const int SW_SHOW = 5, IDC_ARROW = 32512, GWL_STYLE = -16;
    public const uint SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200, MONITOR_DEFAULTTONEAREST = 2;
    public const uint RID_INPUT = 0x10000003, RIM_TYPEMOUSE = 0;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Explicit)]
    public struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort usFlags;
        [FieldOffset(4)] public ushort usButtonFlags;
        [FieldOffset(6)] public ushort usButtonData;
        [FieldOffset(8)] public uint ulRawButtons;
        [FieldOffset(12)] public int lLastX;
        [FieldOffset(16)] public int lLastY;
        [FieldOffset(20)] public uint ulExtraInformation;
    }
    [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTMOUSE { public RAWINPUTHEADER header; public RAWMOUSE mouse; }

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32")] public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32")] public static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32")] public static extern void PostQuitMessage(int code);
    [DllImport("user32")] public static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport("user32")] public static extern int ShowCursor(bool show);
    [DllImport("user32")] public static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
    [DllImport("user32")] public static extern bool ClipCursor(RECT* r);
    [DllImport("user32")] public static extern IntPtr SetCapture(IntPtr hWnd);
    [DllImport("user32")] public static extern bool ReleaseCapture();
    [DllImport("user32")] public static extern int GetWindowLongW(IntPtr hWnd, int idx);
    [DllImport("user32")] public static extern int SetWindowLongW(IntPtr hWnd, int idx, int val);
    [DllImport("user32")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32")] public static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32")] public static extern bool AdjustWindowRect(ref RECT r, uint style, bool menu);
    [DllImport("user32")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devs, uint n, uint size);
    [DllImport("user32")] public static extern uint GetRawInputData(IntPtr hRaw, uint cmd, void* data, ref uint size, uint headerSize);
    [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(IntPtr hWnd, string text);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("winmm")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("user32")] public static extern short GetKeyState(int vk);
    [DllImport("user32")] public static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32")] public static extern bool CloseClipboard();
    [DllImport("user32")] public static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32")] public static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32")] public static extern bool GlobalUnlock(IntPtr h);

    /// <summary>The clipboard's text (first line), or null.</summary>
    public static string ClipboardText(IntPtr hwnd)
    {
        if (!OpenClipboard(hwnd)) return null;
        try
        {
            var h = GetClipboardData(13 /* CF_UNICODETEXT */); if (h == IntPtr.Zero) return null;
            var p = GlobalLock(h); if (p == IntPtr.Zero) return null;
            try { var s = Marshal.PtrToStringUni(p); int nl = s?.IndexOfAny(new[] { '\r', '\n' }) ?? -1; return nl >= 0 ? s[..nl] : s; }
            finally { GlobalUnlock(h); }
        }
        finally { CloseClipboard(); }
    }
    [DllImport("winmm")] public static extern uint timeEndPeriod(uint ms);

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    public static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);
    public static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
}
