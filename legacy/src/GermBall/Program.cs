using System;
using GermBall.Platform;

namespace GermBall;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Germ Ball runs on Windows 10 or 11 only.");
            return 1;
        }
        Paths.Init(args);
        Log.Init();
        try
        {
            using var app = new App(args);
            app.Run();
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(e.ToString());
            Win32.MessageBoxW(IntPtr.Zero, "Germ Ball hit an error and has to close.\n\n" + e.Message + "\n\nA log is in:\n" + Paths.Logs, "Germ Ball", 0x10);
            return 2;
        }
    }
}
