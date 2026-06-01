using Avalonia;
using System;
using System.IO;

namespace MemoriaPlus;

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
            // Volcado de crash equivalente al handler de la app WPF original.
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "MemoriaPlus_crash.log");
                File.WriteAllText(path,
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
