using System.Diagnostics;

namespace Shared.UI.Services;

/// <summary>
/// Abre un archivo en su aplicación por defecto vía el shell del SO
/// (xdg-open en Linux, start en Windows, open en macOS) — portable con
/// <c>UseShellExecute=true</c>.
/// </summary>
public sealed class ProcessFileLauncher : IFileLauncher
{
    public void OpenInDefaultApp(string path)
        => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
