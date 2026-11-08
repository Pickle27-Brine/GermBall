using System;
using System.Runtime.InteropServices;

namespace GermBall.Win;

/// <summary>A plain Win32 window: resizable, borderless fullscreen on F11 or Alt+Enter, raw mouse input for look.</summary>
public sealed unsafe class Window
{
    public IntPtr Handle { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Closed { get; private set; }
    public bool Resized;
    public bool Focused { get; private set; } = true;
    bool fullscreen; Win32.RECT savedRect; int savedStyle;
    readonly Win32.WndProc proc;   // keep the delegate alive for the lifetime of the window

    /// <summary>Editors keep the cursor visible and handle F11 themselves.</summary>
    public bool AllowFullscreenKey = true;

    public Window(string title, int w, int h)
    {
        try { Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
        proc = WndProc;
        var inst = Win32.GetModuleHandleW(null);
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(), style = 0x0003 | 0x0008 /* CS_DBLCLKS */, lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = inst, hCursor = Win32.LoadCursorW(IntPtr.Zero, new IntPtr(Win32.IDC_ARROW)), lpszClassName = "GermBall2",
        };
        Win32.RegisterClassExW(ref wc);
        var r = new Win32.RECT { Right = w, Bottom = h };
        Win32.AdjustWindowRect(ref r, Win32.WS_OVERLAPPEDWINDOW, false);
        Handle = Win32.CreateWindowExW(0, "GermBall2", title, Win32.WS_OVERLAPPEDWINDOW, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
            r.Right - r.Left, r.Bottom - r.Top, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        var mouse = new Win32.RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = 0, hwndTarget = Handle };
        Win32.RegisterRawInputDevices(new[] { mouse }, 1, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>());
        Win32.ShowWindow(Handle, Win32.SW_SHOW);
        UpdateSize();
    }

    void UpdateSize() { Win32.GetClientRect(Handle, out var r); Width = Math.Max(1, r.Right - r.Left); Height = Math.Max(1, r.Bottom - r.Top); }

    /// <summary>Handles pending window messages. Returns false once the window is closed.</summary>
    public bool Pump()
    {
        while (Win32.PeekMessageW(out var m, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
        {
            if (m.message == Win32.WM_QUIT) { Closed = true; break; }
            Win32.TranslateMessage(ref m); Win32.DispatchMessageW(ref m);
        }
        return !Closed;
    }

    public void SetTitle(string t) => Win32.SetWindowTextW(Handle, t);

    public void ToggleFullscreen()
    {
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            savedStyle = Win32.GetWindowLongW(Handle, Win32.GWL_STYLE); Win32.GetWindowRect(Handle, out savedRect);
            var mi = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
            Win32.GetMonitorInfoW(Win32.MonitorFromWindow(Handle, Win32.MONITOR_DEFAULTTONEAREST), ref mi);
            Win32.SetWindowLongW(Handle, Win32.GWL_STYLE, unchecked((int)(Win32.WS_POPUP | Win32.WS_VISIBLE)));
            var m = mi.rcMonitor;
            Win32.SetWindowPos(Handle, IntPtr.Zero, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_NOOWNERZORDER);
        }
        else
        {
            Win32.SetWindowLongW(Handle, Win32.GWL_STYLE, savedStyle);
            var r = savedRect;
            Win32.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_NOOWNERZORDER);
        }
    }

    IntPtr WndProc(IntPtr h, uint msg, IntPtr wp, IntPtr lp)
    {
        switch (msg)
        {
            case Win32.WM_CLOSE: Closed = true; Win32.PostQuitMessage(0); return IntPtr.Zero;
            case Win32.WM_DESTROY: Closed = true; return IntPtr.Zero;
            case Win32.WM_SIZE: UpdateSize(); Resized = true; return IntPtr.Zero;
            case Win32.WM_KILLFOCUS: Focused = false; Input.ReleaseAll(); break;
            case Win32.WM_SETFOCUS: Focused = true; break;
            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
                {
                    int vk = (int)wp; bool repeat = (((long)lp >> 30) & 1) != 0;
                    if (!repeat && AllowFullscreenKey && ((vk == 0x0D && msg == Win32.WM_SYSKEYDOWN) || vk == 0x7A)) { ToggleFullscreen(); return IntPtr.Zero; }   // Alt+Enter, F11
                    if (!repeat) Input.Key(vk, true); else Input.Repeat(vk);
                    if (msg == Win32.WM_SYSKEYDOWN && vk != 0x73) return IntPtr.Zero;   // keep Alt / F10 from opening the system menu, but allow Alt+F4
                    break;
                }
            case Win32.WM_KEYUP:
            case Win32.WM_SYSKEYUP:
                Input.Key((int)wp, false);
                if (msg == Win32.WM_SYSKEYUP) return IntPtr.Zero;
                break;
            case Win32.WM_MOUSEMOVE: Input.MouseMove(Win32.LoWord(lp), Win32.HiWord(lp)); break;
            case Win32.WM_CHAR: { char ch = (char)(long)wp; if (ch >= 32 && ch != 127) Input.Typed.Append(ch); return IntPtr.Zero; }
            case Win32.WM_LBUTTONDBLCLK: Win32.SetCapture(h); Input.Key(Input.VK_LBUTTON, true); Input.DoubleClicked = true; break;
            case Win32.WM_LBUTTONDOWN: Win32.SetCapture(h); Input.Key(Input.VK_LBUTTON, true); break;
            case Win32.WM_LBUTTONUP: Win32.ReleaseCapture(); Input.Key(Input.VK_LBUTTON, false); break;
            case Win32.WM_RBUTTONDOWN: Input.Key(Input.VK_RBUTTON, true); break;
            case Win32.WM_RBUTTONUP: Input.Key(Input.VK_RBUTTON, false); break;
            case Win32.WM_MBUTTONDOWN: Input.Key(Input.VK_MBUTTON, true); break;
            case Win32.WM_MBUTTONUP: Input.Key(Input.VK_MBUTTON, false); break;
            case Win32.WM_XBUTTONDOWN: Input.Key(Win32.HiWord(wp) == 1 ? Input.VK_XBUTTON1 : Input.VK_XBUTTON2, true); return new IntPtr(1);
            case Win32.WM_XBUTTONUP: Input.Key(Win32.HiWord(wp) == 1 ? Input.VK_XBUTTON1 : Input.VK_XBUTTON2, false); return new IntPtr(1);
            case Win32.WM_MOUSEWHEEL: Input.Wheel += Win32.HiWord(wp) / 120f; break;
            case Win32.WM_INPUT:
                {
                    uint size = (uint)sizeof(Win32.RAWINPUTMOUSE);
                    Win32.RAWINPUTMOUSE raw;
                    if (Win32.GetRawInputData(lp, Win32.RID_INPUT, &raw, ref size, (uint)sizeof(Win32.RAWINPUTHEADER)) != unchecked((uint)-1)
                        && raw.header.dwType == Win32.RIM_TYPEMOUSE && (raw.mouse.usFlags & 1) == 0)
                    { Input.MouseDX += raw.mouse.lLastX; Input.MouseDY += raw.mouse.lLastY; }
                    break;
                }
        }
        return Win32.DefWindowProcW(h, msg, wp, lp);
    }
}
