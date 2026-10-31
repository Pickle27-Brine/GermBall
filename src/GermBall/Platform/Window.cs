using System;
using System.Runtime.InteropServices;

namespace GermBall.Platform;

/// <summary>A plain Win32 window with borderless-fullscreen toggle (F11 / Alt+Enter) and raw mouse input.</summary>
public sealed unsafe class Window
{
    public IntPtr Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool Closed { get; private set; }
    public bool Resized;
    public bool Fullscreen { get; private set; }
    readonly Win32.WndProc proc;   // keep the delegate alive
    Win32.RECT savedRect; int savedStyle;

    public Window(string title, int w, int h)
    {
        try { Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); } catch { }
        proc = WndProc;
        var inst = Win32.GetModuleHandleW(null);
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(), style = 0x0002 | 0x0001 /* CS_HREDRAW|CS_VREDRAW */, lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = inst, hCursor = Win32.LoadCursorW(IntPtr.Zero, new IntPtr(Win32.IDC_ARROW)), lpszClassName = "GermBallWindow",
        };
        Win32.RegisterClassExW(ref wc);
        var r = new Win32.RECT { Right = w, Bottom = h };
        Win32.AdjustWindowRect(ref r, Win32.WS_OVERLAPPEDWINDOW, false);
        Handle = Win32.CreateWindowExW(0, "GermBallWindow", title, Win32.WS_OVERLAPPEDWINDOW, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT, r.Right - r.Left, r.Bottom - r.Top, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        Win32.RegisterRawInputDevices(new[] { new Win32.RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = 0, hwndTarget = Handle } }, 1, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>());
        Win32.ShowWindow(Handle, Win32.SW_SHOW); Win32.UpdateWindow(Handle);
        UpdateSize();
    }

    void UpdateSize() { Win32.GetClientRect(Handle, out var r); Width = Math.Max(1, r.Right - r.Left); Height = Math.Max(1, r.Bottom - r.Top); }

    /// <summary>Pumps the message queue. Returns false once the window has been closed.</summary>
    public bool Pump()
    {
        while (Win32.PeekMessageW(out var m, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
        {
            if (m.message == Win32.WM_QUIT) { Closed = true; return false; }
            Win32.TranslateMessage(ref m); Win32.DispatchMessageW(ref m);
        }
        return !Closed;
    }

    public void SetTitle(string t) => Win32.SetWindowTextW(Handle, t);

    public void ToggleFullscreen() => SetFullscreen(!Fullscreen);
    public void SetFullscreen(bool on)
    {
        if (on == Fullscreen) return;
        Fullscreen = on;
        if (on)
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
            case Win32.WM_KILLFOCUS: Input.Focused = false; Input.ReleaseAll(); break;
            case Win32.WM_SETFOCUS: Input.Focused = true; break;
            case Win32.WM_KEYDOWN:
            case Win32.WM_SYSKEYDOWN:
                {
                    int vk = (int)wp;
                    if (vk == 0x0D && msg == Win32.WM_SYSKEYDOWN) { ToggleFullscreen(); return IntPtr.Zero; }   // Alt+Enter
                    if (vk == 0x7A && (((long)lp >> 30) & 1) == 0) ToggleFullscreen();                          // F11
                    Input.KeyEvent(Input.CodeFromVk(vk, lp), true);
                    if (msg == Win32.WM_SYSKEYDOWN && vk != 0x73) return IntPtr.Zero;   // keep Alt from opening the system menu (but let Alt+F4 through)
                    break;
                }
            case Win32.WM_KEYUP:
            case Win32.WM_SYSKEYUP: Input.KeyEvent(Input.CodeFromVk((int)wp, lp), false); if (msg == Win32.WM_SYSKEYUP) return IntPtr.Zero; break;
            case Win32.WM_CHAR: { char c = (char)(long)wp; if (c >= 32) Input.Typed.Append(c); return IntPtr.Zero; }
            case Win32.WM_MOUSEMOVE: Input.MouseX = Win32.LoWord(lp); Input.MouseY = Win32.HiWord(lp); break;
            case Win32.WM_LBUTTONDOWN: Win32.SetCapture(h); Input.KeyEvent("Mouse0", true); break;
            case Win32.WM_LBUTTONUP: Win32.ReleaseCapture(); Input.KeyEvent("Mouse0", false); break;
            case Win32.WM_MBUTTONDOWN: Input.KeyEvent("Mouse1", true); break;
            case Win32.WM_MBUTTONUP: Input.KeyEvent("Mouse1", false); break;
            case Win32.WM_RBUTTONDOWN: Input.KeyEvent("Mouse2", true); break;
            case Win32.WM_RBUTTONUP: Input.KeyEvent("Mouse2", false); break;
            case Win32.WM_XBUTTONDOWN: Input.KeyEvent(Win32.HiWord(wp) == 1 ? "Mouse3" : "Mouse4", true); return new IntPtr(1);
            case Win32.WM_XBUTTONUP: Input.KeyEvent(Win32.HiWord(wp) == 1 ? "Mouse3" : "Mouse4", false); return new IntPtr(1);
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
