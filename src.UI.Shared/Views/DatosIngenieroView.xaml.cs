using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class DatosIngenieroView : UserControl
{
    /// <summary>Extensiones de imagen aceptadas en el drop-zone.</summary>
    private static readonly string[] _extensionesValidas = { ".png", ".jpg", ".jpeg" };

    public DatosIngenieroView() => InitializeComponent();

    // -----------------------------------------------------------------
    // DRAG-DROP — visual feedback compartido por las 2 zonas
    // -----------------------------------------------------------------

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is not Border zone) return;
        if (EsArchivoImagenValido(e))
        {
            zone.BorderBrush = (System.Windows.Media.Brush?)Application.Current.Resources["PrimaryBrush"];
            zone.BorderThickness = new Thickness(2);
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = EsArchivoImagenValido(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is not Border zone) return;
        zone.BorderBrush = (System.Windows.Media.Brush?)Application.Current.Resources["OutlineVariantBrush"];
        zone.BorderThickness = new Thickness(1);
    }

    private static bool EsArchivoImagenValido(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return false;
        var ext = Path.GetExtension(files[0]).ToLowerInvariant();
        return _extensionesValidas.Contains(ext);
    }

    private static string? ObtenerPathDropeado(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            return files[0];
        return null;
    }

    // -----------------------------------------------------------------
    // DROP HANDLERS
    // -----------------------------------------------------------------

    private void FirmaDropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone_DragLeave(sender, e);
        var path = ObtenerPathDropeado(e);
        if (path is null || DataContext is not ConfiguracionViewModel vm) return;
        vm.Perfil.FirmaPath = path;
    }

    private void SelloDropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone_DragLeave(sender, e);
        var path = ObtenerPathDropeado(e);
        if (path is null || DataContext is not ConfiguracionViewModel vm) return;
        vm.Perfil.SelloPath = path;
    }

    // -----------------------------------------------------------------
    // BOTONES Examinar / Quitar — alternativa al drag-drop
    // -----------------------------------------------------------------

    private void ExaminarFirma_Click(object sender, RoutedEventArgs e)
        => ExaminarYAplicar(p => { if (DataContext is ConfiguracionViewModel vm) vm.Perfil.FirmaPath = p; });

    private void ExaminarSello_Click(object sender, RoutedEventArgs e)
        => ExaminarYAplicar(p => { if (DataContext is ConfiguracionViewModel vm) vm.Perfil.SelloPath = p; });

    private void QuitarFirma_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguracionViewModel vm) vm.Perfil.FirmaPath = "";
    }

    private void QuitarSello_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguracionViewModel vm) vm.Perfil.SelloPath = "";
    }

    private static void ExaminarYAplicar(Action<string> apply)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Seleccionar imagen",
            Filter = "Imágenes (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true) apply(dlg.FileName);
    }
}
