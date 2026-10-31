using System;
using System.Runtime.InteropServices;

namespace GermBall.Platform;

/// <summary>The standard Windows Open / Save dialogs, so germs, settings and key maps can be imported from and exported to any path.</summary>
public static class FileDialogs
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OPENFILENAME
    {
        public int lStructSize; public IntPtr hwndOwner, hInstance; public string lpstrFilter; public IntPtr lpstrCustomFilter; public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile; public int nMaxFile; public IntPtr lpstrFileTitle; public int nMaxFileTitle; public string lpstrInitialDir, lpstrTitle;
        public int Flags; public short nFileOffset, nFileExtension; public string lpstrDefExt; public IntPtr lCustData, lpfnHook; public string lpTemplateName;
        public IntPtr pvReserved; public int dwReserved, FlagsEx;
    }
    [DllImport("comdlg32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);
    [DllImport("comdlg32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetSaveFileNameW(ref OPENFILENAME ofn);

    const int OFN_OVERWRITEPROMPT = 0x2, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000, OFN_EXPLORER = 0x80000;
    public const string GermFilter = "Germ files (*.germ.json)\0*.germ.json\0JSON files (*.json)\0*.json\0All files (*.*)\0*.*\0\0";
    public const string JsonFilter = "JSON files (*.json)\0*.json\0All files (*.*)\0*.*\0\0";

    public static string Open(IntPtr owner, string title, string filter, string initialDir) => Run(owner, title, filter, initialDir, null, false);
    public static string Save(IntPtr owner, string title, string filter, string initialDir, string defaultName) => Run(owner, title, filter, initialDir, defaultName, true);

    static string Run(IntPtr owner, string title, string filter, string dir, string name, bool save)
    {
        const int max = 4096;
        IntPtr buf = Marshal.AllocHGlobal(max * 2);
        try
        {
            var init = (name ?? "") + "\0";
            Marshal.Copy(init.ToCharArray(), 0, buf, init.Length);
            var o = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(), hwndOwner = owner, lpstrFilter = filter, nFilterIndex = 1, lpstrFile = buf, nMaxFile = max,
                lpstrInitialDir = dir, lpstrTitle = title, lpstrDefExt = "json",
                Flags = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
            };
            bool ok = save ? GetSaveFileNameW(ref o) : GetOpenFileNameW(ref o);
            return ok ? Marshal.PtrToStringUni(buf) : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
