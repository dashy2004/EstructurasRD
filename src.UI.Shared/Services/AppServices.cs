using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace MemoriaPlus.Services;

/// <summary>Filtro de archivos para los pickers (nombre visible + patrones glob).</summary>
public readonly record struct FileFilter(string Name, string[] Patterns);

/// <summary>Diálogos de archivo (open/save) — async, sobre TopLevel.StorageProvider.</summary>
public interface IDialogService
{
    Task<string?> OpenFileAsync(string title, params FileFilter[] filters);
    Task<string?> SaveFileAsync(string title, string suggestedName, string defaultExt, params FileFilter[] filters);
}

/// <summary>
/// Resultado de la confirmación de descarte de 3 estados (Fase A — pérdida de
/// datos). <see cref="Cancelar"/> es el valor por defecto/seguro: significa
/// «no hacer nada, quedarse», y es lo que devuelve un descarte del diálogo
/// (Escape / botón X de la ventana).
/// </summary>
public enum ResultadoDescarte
{
    Guardar,
    Descartar,
    Cancelar,
}

/// <summary>Cuadros de mensaje (reemplaza System.Windows.MessageBox).</summary>
public interface IMessageBoxService
{
    Task<bool> ConfirmYesNoAsync(string title, string message);
    Task InfoAsync(string title, string message);

    /// <summary>
    /// Confirmación de descarte de 3 estados: «Guardar», «Descartar» o «Cancelar».
    /// CRÍTICO (Fase A): descartar el diálogo de cualquier forma (Escape / botón X
    /// de la ventana) devuelve <see cref="ResultadoDescarte.Cancelar"/> = quedarse,
    /// nunca descarta el trabajo de forma silenciosa.
    /// </summary>
    Task<ResultadoDescarte> ConfirmarGuardarDescartarCancelarAsync(string titulo, string mensaje);
}

/// <summary>Portapapeles (reemplaza System.Windows.Clipboard).</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>Abrir un archivo en su app por defecto (xdg-open / shell).</summary>
public interface IFileLauncher
{
    void OpenInDefaultApp(string path);
}

/// <summary>
/// Localizador estático de servicios de UI. Reemplaza el acceso directo a
/// Microsoft.Win32 / System.Windows.Clipboard / MessageBox desde los ViewModels.
/// La app asigna las implementaciones y <see cref="TopLevelAccessor"/> al
/// arrancar (MainWindow lo registra como <c>() =&gt; this</c>), de modo que los
/// servicios resuelven el TopLevel activo de forma perezosa.
/// </summary>
public static class AppServices
{
    /// <summary>Devuelve el <see cref="TopLevel"/> activo (ventana principal).</summary>
    public static Func<TopLevel?> TopLevelAccessor { get; set; } = () => null;

    public static IDialogService Dialogs { get; set; } = new AvaloniaDialogService();
    public static IMessageBoxService MessageBox { get; set; } = new AvaloniaMessageBoxService();
    public static IClipboardService Clipboard { get; set; } = new AvaloniaClipboardService();
    public static IFileLauncher Launcher { get; set; } = new ProcessFileLauncher();

    internal static TopLevel? TopLevel => TopLevelAccessor();
}
