using System;
using System.Numerics;
using GermBall.Sim;
using GermBall.Engine;
using Vortice.XInput;

namespace GermBall.Win;

/// <summary>Keyboard, mouse and the first XInput controller, mapped onto the game's <see cref="InputFrame"/>.</summary>
public static class Input
{
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;
    const int VK_SHIFT = 0x10, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_F1 = 0x70;

    static readonly bool[] down = new bool[256], pressed = new bool[256], released = new bool[256];
    public static float MouseDX, MouseDY, Wheel;
    public static float MouseSensitivity = 0.0022f;   // radians per raw mouse count
    public static float StickLookSpeed = 3.2f;        // radians per second at full deflection
    public static bool InvertY;
    static GamepadButtons prevPad;
    static bool padConnected; static double nextPadScan;

    internal static void Key(int vk, bool isDown)
    {
        vk &= 0xFF;
        if (isDown && !down[vk]) pressed[vk] = true;
        if (!isDown && down[vk]) released[vk] = true;   // kept even when the press was in the same frame (a quick click)
        down[vk] = isDown;
    }
    internal static void ReleaseAll() => Array.Clear(down);
    internal static void Repeat(int vk) { vk &= 0xFF; if (vk == 0x08 || vk == 0x2E || (vk >= 0x25 && vk <= 0x28)) pressed[vk] = true; }   // held editing keys repeat
    internal static void MouseMove(int x, int y) { CursorX = x; CursorY = y; }
    public static int CursorX, CursorY;
    internal static readonly System.Text.StringBuilder Typed = new();
    internal static bool DoubleClicked;
    static int lastUiX, lastUiY;

    /// <summary>The editors' view of the mouse and keyboard this frame.</summary>
    public static void ReadUi(UiInput u)
    {
        u.ClearFrame();
        u.MouseDX = CursorX - lastUiX; u.MouseDY = CursorY - lastUiY; lastUiX = CursorX; lastUiY = CursorY;
        u.MouseX = CursorX; u.MouseY = CursorY; u.Wheel = Wheel;
        int[] vks = { VK_LBUTTON, VK_RBUTTON, VK_MBUTTON };
        for (int i = 0; i < 3; i++) { u.Down[i] = down[vks[i]]; u.Pressed[i] = pressed[vks[i]]; u.Released[i] = released[vks[i]]; }
        u.Ctrl = down[0x11]; u.Shift = down[0x10]; u.Alt = down[0x12];
        u.KeysDown.Clear();
        for (int k = 8; k < 256; k++) { if (down[k]) u.KeysDown.Add(k); if (pressed[k]) u.KeysPressed.Add(k); }
        u.Typed = Typed.ToString(); Typed.Clear();   // WM_CHAR only brings printable characters (and Ctrl+V pastes)
        u.DoubleClick = DoubleClicked; DoubleClicked = false;
    }

    static bool D(int vk) => down[vk];
    static bool P(int vk) => pressed[vk];
    static bool DK(char c) => down[c];
    static bool PK(char c) => pressed[c];

    /// <summary>Builds this frame's input. Mouse look only counts while the cursor is captured.</summary>
    public static InputFrame Read(float dt, bool mouseCaptured, double now)
    {
        var f = new InputFrame();

        // ---- keyboard and mouse
        float mx = (DK('D') || D(VK_RIGHT) ? 1 : 0) - (DK('A') || D(VK_LEFT) ? 1 : 0);
        float my = (DK('W') || D(VK_UP) ? 1 : 0) - (DK('S') || D(VK_DOWN) ? 1 : 0);
        f.Move = new Vector2(mx, my);
        if (f.Move.LengthSquared() > 1) f.Move = Vector2.Normalize(f.Move);
        if (mouseCaptured) f.Look = new Vector2(MouseDX, MouseDY * (InvertY ? 1 : -1)) * MouseSensitivity;

        void Map(Btn b, bool held, bool hit) { if (held) f.Held |= b; if (hit) f.Pressed |= b; }
        Map(Btn.Jump, D(VK_SPACE), P(VK_SPACE));
        Map(Btn.Roll, D(VK_SHIFT) || DK('C'), P(VK_SHIFT) || PK('C'));
        Map(Btn.Fire, (D(VK_LBUTTON) && mouseCaptured) || DK('F'), (P(VK_LBUTTON) && mouseCaptured) || PK('F'));
        Map(Btn.Aim, D(VK_RBUTTON), P(VK_RBUTTON));
        Map(Btn.Punch, DK('Q') || D(VK_XBUTTON1), PK('Q') || P(VK_XBUTTON1));
        Map(Btn.Reload, DK('R'), PK('R'));
        Map(Btn.Weapon1, false, PK('1')); Map(Btn.Weapon2, false, PK('2')); Map(Btn.Weapon3, false, PK('3'));
        Map(Btn.WeaponNext, false, Wheel < 0); Map(Btn.WeaponPrev, false, Wheel > 0);
        Map(Btn.Help, false, P(VK_F1)); Map(Btn.Debug, false, P(VK_F1 + 1)); Map(Btn.ShowZones, false, P(VK_F1 + 2)); Map(Btn.Noclip, false, P(VK_F1 + 3));
        Map(Btn.Respawn, false, P(VK_F1 + 4)); Map(Btn.NextZone, false, P(VK_F1 + 5)); Map(Btn.PrevZone, false, P(VK_F1 + 6));
        Map(Btn.God, false, P(VK_F1 + 7)); Map(Btn.SlowMo, false, P(VK_F1 + 8)); Map(Btn.Pause, false, P(VK_ESCAPE));
        Map(Btn.MusicNext, false, PK('N')); Map(Btn.MusicPrev, false, PK('B')); Map(Btn.MusicLoop, false, PK('L')); Map(Btn.MusicPause, false, PK('M'));

        // ---- controller (first one connected; empty slots are only polled once a second because that's slow)
        if (padConnected || now >= nextPadScan)
        {
            padConnected = XInput.GetState(0, out var st);
            if (!padConnected) nextPadScan = now + 1;
            else
            {
                var g = st.Gamepad; var b = g.Buttons;
                var ls = Stick(g.LeftThumbX, g.LeftThumbY, 0.22f);
                if (ls.LengthSquared() > f.Move.LengthSquared()) f.Move = ls;
                var rs = Stick(g.RightThumbX, g.RightThumbY, 0.15f);
                // a gentle curve: fine aim near the centre, full speed at the edge
                float rl = rs.Length(); if (rl > 0) rs *= rl;
                float lookSpeed = StickLookSpeed * (g.LeftTrigger > 60 ? 0.55f : 1f);
                f.Look += new Vector2(rs.X, rs.Y * (InvertY ? -1 : 1)) * lookSpeed * dt;
                bool Has(GamepadButtons x) => (b & x) != 0;
                bool Hit(GamepadButtons x) => (b & x) != 0 && (prevPad & x) == 0;
                void Pad(Btn x, GamepadButtons gb) { if (Has(gb)) f.Held |= x; if (Hit(gb)) f.Pressed |= x; }
                Pad(Btn.Jump, GamepadButtons.A); Pad(Btn.Roll, GamepadButtons.B); Pad(Btn.Reload, GamepadButtons.X);
                Pad(Btn.WeaponNext, GamepadButtons.Y); Pad(Btn.Punch, GamepadButtons.RightShoulder);
                Pad(Btn.Pause, GamepadButtons.Start); Pad(Btn.Help, GamepadButtons.Back);
                Pad(Btn.NextZone, GamepadButtons.DPadUp); Pad(Btn.PrevZone, GamepadButtons.DPadDown);
                Pad(Btn.Respawn, GamepadButtons.DPadLeft); Pad(Btn.Debug, GamepadButtons.DPadRight);
                bool rt = g.RightTrigger > 40, lt = g.LeftTrigger > 40;
                if (rt) { f.Held |= Btn.Fire; if (!prevRt) f.Pressed |= Btn.Fire; }
                if (lt) f.Held |= Btn.Aim;
                prevRt = rt; prevPad = b;
            }
        }
        return f;
    }
    static bool prevRt;

    static Vector2 Stick(short x, short y, float dead)
    {
        var v = new Vector2(x / 32767f, y / 32767f);
        float l = v.Length();
        if (l < dead) return Vector2.Zero;
        return v / l * MathF.Min(1, (l - dead) / (1 - dead));
    }

    public static void EndFrame() { Array.Clear(pressed); Array.Clear(released); MouseDX = MouseDY = 0; Wheel = 0; Typed.Clear(); DoubleClicked = false; }

    // ---- cursor capture for mouse look
    public static bool Captured { get; private set; }
    public static unsafe void Capture(Window w, bool on)
    {
        if (on == Captured) return;
        Captured = on;
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
    /// <summary>True when the left button was clicked this frame (used to recapture the mouse).</summary>
    public static bool Clicked => pressed[VK_LBUTTON];
}
