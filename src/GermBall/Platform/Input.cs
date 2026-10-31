using System;
using System.Collections.Generic;
using System.Text;
using Vortice.XInput;

namespace GermBall.Platform;

/// <summary>
/// Keyboard and mouse straight from Win32 (raw input for unaccelerated mouse look), controllers through XInput.
/// Keys use the same names the original bindings used ("KeyW", "Space", "ShiftLeft", "Mouse0"...) so key maps stay readable.
/// </summary>
public static class Input
{
    static readonly HashSet<string> down = new(), pressed = new(), released = new();
    public static float MouseX, MouseY, MouseDX, MouseDY, Wheel;
    public static readonly StringBuilder Typed = new();
    /// <summary>The next key or button pressed, for the key-binding screen.</summary>
    public static string LastPressed;
    public static bool Locked { get; private set; }
    public static bool Focused = true;

    public static bool Down(string code) => !string.IsNullOrEmpty(code) && down.Contains(code);
    public static bool Pressed(string code) => !string.IsNullOrEmpty(code) && pressed.Contains(code);
    public static bool Released(string code) => !string.IsNullOrEmpty(code) && released.Contains(code);
    public static bool AnyDown => down.Count > 0;

    internal static void KeyEvent(string code, bool isDown)
    {
        if (code == null) return;
        if (isDown) { if (down.Add(code)) { pressed.Add(code); LastPressed = code; } }
        else if (down.Remove(code)) released.Add(code);
    }
    internal static void ReleaseAll() { foreach (var k in down) released.Add(k); down.Clear(); }

    /// <summary>Called once per frame after the simulation has consumed this frame's edges.</summary>
    public static void EndFrame()
    {
        pressed.Clear(); released.Clear(); MouseDX = MouseDY = 0; Wheel = 0; Typed.Clear();
        Gamepad.EndFrame();
    }

    /// <summary>Pointer lock: hide and confine the cursor to the window (mouse look uses raw deltas).</summary>
    public static unsafe void Lock(Window w, bool on)
    {
        if (on == Locked) return;
        Locked = on;
        if (on)
        {
            Win32.GetClientRect(w.Handle, out var r);
            var tl = new Win32.POINT { X = r.Left, Y = r.Top }; var br = new Win32.POINT { X = r.Right, Y = r.Bottom };
            Win32.ClientToScreen(w.Handle, ref tl); Win32.ClientToScreen(w.Handle, ref br);
            var clip = new Win32.RECT { Left = tl.X, Top = tl.Y, Right = br.X, Bottom = br.Y };
            Win32.ClipCursor(&clip);
            while (Win32.ShowCursor(false) >= 0) { }
        }
        else
        {
            Win32.ClipCursor(null);
            while (Win32.ShowCursor(true) < 0) { }
        }
    }

    // ---- virtual-key → code names
    static readonly Dictionary<int, string> VK = new();
    static Input()
    {
        for (int c = 'A'; c <= 'Z'; c++) VK[c] = "Key" + (char)c;
        for (int c = '0'; c <= '9'; c++) VK[c] = "Digit" + (char)c;
        for (int i = 0; i < 10; i++) VK[0x60 + i] = "Numpad" + i;
        for (int i = 1; i <= 24; i++) VK[0x6F + i] = "F" + i;
        VK[0x20] = "Space"; VK[0x0D] = "Enter"; VK[0x1B] = "Escape"; VK[0x08] = "Backspace"; VK[0x09] = "Tab";
        VK[0x25] = "ArrowLeft"; VK[0x26] = "ArrowUp"; VK[0x27] = "ArrowRight"; VK[0x28] = "ArrowDown";
        VK[0x2D] = "Insert"; VK[0x2E] = "Delete"; VK[0x24] = "Home"; VK[0x23] = "End"; VK[0x21] = "PageUp"; VK[0x22] = "PageDown";
        VK[0x14] = "CapsLock"; VK[0xBA] = "Semicolon"; VK[0xBB] = "Equal"; VK[0xBC] = "Comma"; VK[0xBD] = "Minus"; VK[0xBE] = "Period"; VK[0xBF] = "Slash";
        VK[0xC0] = "Backquote"; VK[0xDB] = "BracketLeft"; VK[0xDC] = "Backslash"; VK[0xDD] = "BracketRight"; VK[0xDE] = "Quote";
        VK[0x6A] = "NumpadMultiply"; VK[0x6B] = "NumpadAdd"; VK[0x6D] = "NumpadSubtract"; VK[0x6E] = "NumpadDecimal"; VK[0x6F] = "NumpadDivide";
    }
    internal static string CodeFromVk(int vk, IntPtr lParam)
    {
        bool ext = (((long)lParam >> 24) & 1) != 0; int scan = (int)(((long)lParam >> 16) & 0xFF);
        switch (vk)
        {
            case 0x10: return scan == 0x36 ? "ShiftRight" : "ShiftLeft";
            case 0x11: return ext ? "ControlRight" : "ControlLeft";
            case 0x12: return ext ? "AltRight" : "AltLeft";
            case 0x0D: return ext ? "NumpadEnter" : "Enter";
        }
        return VK.TryGetValue(vk, out var s) ? s : null;
    }

    /// <summary>A short label for a key code, for the HUD and the key-binding screen.</summary>
    public static string Label(string code)
    {
        if (string.IsNullOrEmpty(code)) return "—";
        if (code.StartsWith("Key")) return code[3..];
        if (code.StartsWith("Digit")) return code[5..];
        return code switch
        {
            "Mouse0" => "Left click", "Mouse1" => "Middle click", "Mouse2" => "Right click", "Mouse3" => "Mouse 4", "Mouse4" => "Mouse 5",
            "ShiftLeft" => "L Shift", "ShiftRight" => "R Shift", "ControlLeft" => "L Ctrl", "ControlRight" => "R Ctrl", "AltLeft" => "L Alt", "AltRight" => "R Alt",
            "ArrowUp" => "↑", "ArrowDown" => "↓", "ArrowLeft" => "←", "ArrowRight" => "→", "Space" => "Space",
            _ => code,
        };
    }
}

/// <summary>
/// XInput controllers presented in the W3C "standard gamepad" layout the original read through the Gamepad API:
/// buttons 0 A, 1 B, 2 X, 3 Y, 4 LB, 5 RB, 6 LT, 7 RT, 8 Back, 9 Start, 10 LS, 11 RS, 12-15 D-pad, 16 Guide; axes 0/1 left stick, 2/3 right stick.
/// </summary>
public static class Gamepad
{
    public sealed class Pad
    {
        public bool Connected; public int Index;
        public readonly bool[] Buttons = new bool[17];
        public readonly float[] Value = new float[17];
        public readonly float[] Axes = new float[4];
        public bool B(int i) => Buttons[i];
    }
    public static readonly Pad[] Pads = { new() { Index = 0 }, new() { Index = 1 }, new() { Index = 2 }, new() { Index = 3 } };
    static float rumbleT, rumbleLo, rumbleHi;
    static double nextScan;

    /// <summary>The first connected controller, or null.</summary>
    public static Pad First { get { foreach (var p in Pads) if (p.Connected) return p; return null; } }

    public static void Poll(double now)
    {
        for (int i = 0; i < 4; i++)
        {
            var p = Pads[i];
            // polling a slot with nothing in it is slow, so empty slots are rechecked once a second
            if (!p.Connected && now < nextScan) continue;
            if (!XInput.GetState((uint)i, out var st)) { p.Connected = false; Array.Clear(p.Buttons); Array.Clear(p.Axes); continue; }
            p.Connected = true;
            var g = st.Gamepad; var b = g.Buttons;
            void Set(int k, bool v) { p.Buttons[k] = v; p.Value[k] = v ? 1 : 0; }
            Set(0, b.HasFlag(GamepadButtons.A)); Set(1, b.HasFlag(GamepadButtons.B)); Set(2, b.HasFlag(GamepadButtons.X)); Set(3, b.HasFlag(GamepadButtons.Y));
            Set(4, b.HasFlag(GamepadButtons.LeftShoulder)); Set(5, b.HasFlag(GamepadButtons.RightShoulder));
            float lt = g.LeftTrigger / 255f, rt = g.RightTrigger / 255f;
            p.Value[6] = lt; p.Buttons[6] = lt > 0.12f; p.Value[7] = rt; p.Buttons[7] = rt > 0.12f;
            Set(8, b.HasFlag(GamepadButtons.Back)); Set(9, b.HasFlag(GamepadButtons.Start));
            Set(10, b.HasFlag(GamepadButtons.LeftThumb)); Set(11, b.HasFlag(GamepadButtons.RightThumb));
            Set(12, b.HasFlag(GamepadButtons.DPadUp)); Set(13, b.HasFlag(GamepadButtons.DPadDown)); Set(14, b.HasFlag(GamepadButtons.DPadLeft)); Set(15, b.HasFlag(GamepadButtons.DPadRight));
            // the Gamepad API has +Y pointing down on the sticks
            p.Axes[0] = Stick(g.LeftThumbX); p.Axes[1] = -Stick(g.LeftThumbY); p.Axes[2] = Stick(g.RightThumbX); p.Axes[3] = -Stick(g.RightThumbY);
        }
        if (now >= nextScan) nextScan = now + 1;
    }
    static float Stick(short v) => Math.Clamp(v / 32767f, -1, 1);

    /// <summary>Rumble on the first pad (hits, explosions). Strength 0..1, duration in seconds.</summary>
    public static void Rumble(float lo, float hi, float dur)
    {
        var p = First; if (p == null) return;
        rumbleLo = Math.Max(rumbleLo, lo); rumbleHi = Math.Max(rumbleHi, hi); rumbleT = Math.Max(rumbleT, dur);
        XInput.SetVibration((uint)p.Index, (ushort)(rumbleLo * 65535), (ushort)(rumbleHi * 65535));
    }
    public static void Update(float dt)
    {
        if (rumbleT <= 0) return;
        rumbleT -= dt;
        if (rumbleT <= 0) { rumbleLo = rumbleHi = 0; var p = First; if (p != null) XInput.SetVibration((uint)p.Index, 0, 0); }
    }
    internal static void EndFrame() { }
}
