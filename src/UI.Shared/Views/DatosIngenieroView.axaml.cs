using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Shared.UI.Services;
using Shared.UI.ViewModels;

namespace Shared.UI.Views;

public partial class DatosIngenieroView : UserControl
{
    private static readonly string[] _extensionesValidas = { ".png", ".jpg", ".jpeg" };

    public DatosIngenieroView()
    {
        AvaloniaXamlLoader.Load(this);
        WireDropZone(this.FindControl<Border>("FirmaDropZone"), p => SetVm(vm => vm.Perfil.FirmaPath = p));
        WireDropZone(this.FindControl<Border>("SelloDropZone"), p => SetVm(vm => vm.Perfil.SelloPath = p));
    }

    private void SetVm(Action<ConfiguracionViewModel> apply)
    {
        if (DataContext is ConfiguracionViewModel vm) apply(vm);
    }

    // -----------------------------------------------------------------
    // DRAG-DROP — feedback visual + drop (port de System.Windows.DragDrop)
    // -----------------------------------------------------------------
    private void WireDropZone(Border? zone, Action<string> onDropped)
    {
        if (zone is null) return;
        zone.AddHandler(DragDrop.DragEnterEvent, (_, e) => OnDragOver(zone, e));
        zone.AddHandler(DragDrop.DragOverEvent,  (_, e) => OnDragOver(zone, e));
        zone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ResetZone(zone));
        zone.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            ResetZone(zone);
            var path = PrimerArchivo(e);
            if (path is not null) onDropped(path);
        });
    }

    private void OnDragOver(Border zone, DragEventArgs e)
    {
        if (EsImagenValida(e))
        {
            zone.BorderBrush = Application.Current?.FindResource("PrimaryBrush") as IBrush;
            zone.BorderThickness = new Thickness(2);
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ResetZone(Border zone)
    {
        zone.BorderBrush = Application.Current?.FindResource("OutlineVariantBrush") as IBrush;
        zone.BorderThickness = new Thickness(1);
    }

    private static bool EsImagenValida(DragEventArgs e)
    {
        var path = PrimerArchivo(e);
        if (path is null) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return _extensionesValidas.Contains(ext);
    }

    private static string? PrimerArchivo(DragEventArgs e)
    {
        // CS0618: e.Data (IDataObject) está marcado obsoleto en Avalonia 11.3 a
        // favor de DataTransfer, pero GetFiles() sobre IDataObject sigue siendo
        // la vía soportada en 11.3.x. Se suprime hasta migrar a DataTransfer.
#pragma warning disable CS0618
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        return files?.OfType<IStorageItem>().FirstOrDefault()?.TryGetLocalPath();
    }

    // -----------------------------------------------------------------
    // BOTONES Examinar / Quitar
    // -----------------------------------------------------------------
    private void ExaminarFirma_Click(object? sender, RoutedEventArgs e)
        => _ = ExaminarYAplicar(p => SetVm(vm => vm.Perfil.FirmaPath = p));

    private void ExaminarSello_Click(object? sender, RoutedEventArgs e)
        => _ = ExaminarYAplicar(p => SetVm(vm => vm.Perfil.SelloPath = p));

    private void QuitarFirma_Click(object? sender, RoutedEventArgs e) => SetVm(vm => vm.Perfil.FirmaPath = "");
    private void QuitarSello_Click(object? sender, RoutedEventArgs e) => SetVm(vm => vm.Perfil.SelloPath = "");

    private static async Task ExaminarYAplicar(Action<string> apply)
    {
        var ruta = await AppServices.Dialogs.OpenFileAsync(
            "Seleccionar imagen",
            new FileFilter("Imágenes", new[] { "*.png", "*.jpg", "*.jpeg" }));
        if (ruta is not null) apply(ruta);
    }
}
