using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LosasPlus.Cargas;
using LosasPlus.ViewModels;

namespace LosasPlus.Views;

/// <summary>
/// Vista de la pestaña «Cargas y Combinaciones» (Fase 2, Iteración 2). El
/// code-behind sólo puentea la edición inline de los <see cref="DataGrid"/> al
/// snapshot de Undo del ViewModel: <c>BeginningEdit</c> captura el estado
/// <b>antes</b> de que la celda cambie, de modo que Ctrl+Z lo revierta.
/// </summary>
public partial class CargasCombinacionesView : UserControl
{
    public CargasCombinacionesView() => InitializeComponent();

    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CargasCombinacionesViewModel vm)
            vm.SnapshotAntesDeEditar();
    }

    /// <summary>
    /// Abre un archivo DISEST <c>.DZP</c>/<c>.CEZ</c>, lo parsea y muestra el
    /// modal de importación. Si el usuario aplica, refresca la pestaña.
    /// </summary>
    private void OnImportarClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        var vm = main.CargasCombinaciones;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Importar combinaciones DISEST",
            Filter = "Combinaciones DISEST (*.dzp;*.cez)|*.dzp;*.cez|Todos los archivos (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            // Los archivos DISEST usan codificación Windows-1252 (acentos).
            var texto = File.ReadAllText(dlg.FileName, Encoding.Latin1);
            var formato = dlg.FileName.EndsWith(".cez", StringComparison.OrdinalIgnoreCase)
                ? FormatoCombinaciones.Cez
                : FormatoCombinaciones.Dzp;

            var importador = vm.CrearImportador(texto, formato, Path.GetFileName(dlg.FileName));
            var win = new ImportarCombinacionesWindow
            {
                DataContext = importador,
                Owner       = Window.GetWindow(this),
            };
            importador.Cerrar = win.Close;
            win.ShowDialog();

            if (importador.Aplicado)
                vm.NotificarRestauracion();
        }
        catch (CombinacionesParseException ex)
        {
            MessageBox.Show("No se pudo leer el archivo de combinaciones:\n\n" + ex.Message,
                            "Importar combinaciones", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException ex)
        {
            MessageBox.Show("No se pudo abrir el archivo:\n\n" + ex.Message,
                            "Importar combinaciones", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
