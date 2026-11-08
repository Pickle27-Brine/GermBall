using System.Collections.Generic;

namespace GermBall.Engine;

/// <summary>Mouse and keyboard for the editors' interface, in window pixels. Key codes are Windows virtual-key codes
/// (letters and digits are their ASCII capitals); see <see cref="Keys"/>.</summary>
public sealed class UiInput
{
    public float MouseX, MouseY, MouseDX, MouseDY, Wheel;
    /// <summary>0 left, 1 right, 2 middle.</summary>
    public readonly bool[] Down = new bool[3], Pressed = new bool[3], Released = new bool[3];
    public bool Ctrl, Shift, Alt;
    public readonly HashSet<int> KeysDown = new(), KeysPressed = new();
    public string Typed = "";
    /// <summary>Double click with the left button this frame.</summary>
    public bool DoubleClick;

    public bool Key(int vk) => KeysPressed.Contains(vk);
    public bool Held(int vk) => KeysDown.Contains(vk);

    public void ClearFrame()
    {
        MouseDX = MouseDY = Wheel = 0; Typed = ""; DoubleClick = false;
        for (int i = 0; i < 3; i++) { Pressed[i] = false; Released[i] = false; }
        KeysPressed.Clear();
    }
}

public static class Keys
{
    public const int Backspace = 0x08, Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Space = 0x20, PageUp = 0x21, PageDown = 0x22,
        End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Delete = 0x2E,
        F1 = 0x70, F2 = 0x71, F3 = 0x72, F4 = 0x73, F5 = 0x74;
    public static int Letter(char c) => char.ToUpperInvariant(c);
}
