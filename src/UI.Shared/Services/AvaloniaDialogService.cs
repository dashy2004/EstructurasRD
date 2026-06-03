using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace Shared.UI.Services;

/// <summary>
/// Implementación de <see cref="IDialogService"/> con el StorageProvider de
/// Avalonia (multiplataforma). Reemplaza Microsoft.Win32.OpenFileDialog/
/// SaveFileDialog de WPF. Devuelve el path local del archivo elegido o null si
/// el usuario cancela.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    public async Task<string?> OpenFileAsync(string title, params FileFilter[] filters)
    {
        var top = AppServices.TopLevel;
        if (top is null) return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ToFileTypes(filters),
        });

        var file = files.FirstOrDefault();
        return file?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, string defaultExt, params FileFilter[] filters)
    {
        var top = AppServices.TopLevel;
        if (top is null) return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExt.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = ToFileTypes(filters),
        });

        return file?.TryGetLocalPath();
    }

    private static List<FilePickerFileType>? ToFileTypes(FileFilter[] filters)
    {
        if (filters is null || filters.Length == 0) return null;
        return filters
            .Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns })
            .ToList();
    }
}
