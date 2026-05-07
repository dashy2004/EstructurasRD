using System;
using System.IO;
using System.Windows;

namespace MemoriaPlus;

/// <summary>
/// Punto de entrada de Memoria Plus. Engancha un handler global de
/// excepciones no controladas que vuelca el detalle a
/// <c>%TEMP%\MemoriaPlus_crash.log</c> para facilitar el diagnóstico cuando
/// el shell falla al cargar (XAML invalido, binding errors fatales, etc.).
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "MemoriaPlus_crash.log");
                File.WriteAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled exception:\n\n" +
                    e.Exception.ToString());
            }
            catch { /* el handler nunca debe lanzar */ }
            // No marcamos Handled — dejamos que la app cierre con codigo de
            // error real, asi el smoke test sabe que algo fallo.
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "MemoriaPlus_crash.log");
                File.WriteAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AppDomain unhandled:\n\n" +
                    (e.ExceptionObject?.ToString() ?? "<null>"));
            }
            catch { }
        };
    }
}
