using System.Threading.Tasks;

namespace MemoriaPlus.Services;

/// <summary>
/// Implementación de <see cref="IClipboardService"/> con el portapapeles del
/// TopLevel de Avalonia. Reemplaza System.Windows.Clipboard.
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = AppServices.TopLevel?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }
}
