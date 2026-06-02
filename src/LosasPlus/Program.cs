using Avalonia;
using System;
using System.IO;

namespace LosasPlus;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "LosasPlus_crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled exception:\n\n{ex}");
            }
            catch { /* el handler nunca debe lanzar */ }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
