using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Shared.UI.Services;

/// <summary>Filtro de archivos para los pickers (nombre visible + patrones glob).</summary>
public readonly record struct FileFilter(string Name, string[] Patterns);

/// <summary>Diálogos de archivo (open/save) — async, sobre TopLevel.StorageProvider.</summary>
public interface IDialogService
{
    Task<string?> OpenFileAsync(string title, params FileFilter[] filters);
    Task<string?> SaveFileAsync(string title, string suggestedName, string defaultExt, params FileFilter[] filters);
}

/// <summary>Cuadros de mensaje (reemplaza System.Windows.MessageBox).</summary>
public interface IMessageBoxService
{
    Task<bool> ConfirmYesNoAsync(string title, string message);
    Task InfoAsync(string title, string message);
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
