using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Handlers globales de excepciones — vuelcan a %TEMP%/MemoriaPlus_crash.log
        // (equivalente al DispatcherUnhandledException de la app WPF original).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            VolcarCrash("AppDomain", e.ExceptionObject as Exception);
        Dispatcher.UIThread.UnhandledException += (_, e) =>
            VolcarCrash("UIThread", e.Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void VolcarCrash(string origen, Exception? ex)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "MemoriaPlus_crash.log");
            File.WriteAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origen} unhandled:\n\n{ex}");
        }
        catch { /* el handler nunca debe lanzar */ }
    }
}
