using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LosasPlus.Cargas;
using LosasPlus.ViewModels;

namespace LosasPlus.Views;

/// <summary>
/// Vista de la pestaña «Cargas y Combinaciones» (Fase 2, Iteración 4 — matriz
/// de factores). El code-behind cumple dos funciones que el XAML no puede:
/// <list type="number">
///   <item>Puentea la edición inline de los <see cref="DataGrid"/> al snapshot
///   de Undo del ViewModel — <c>BeginningEdit</c> captura el estado <b>antes</b>
///   de que la celda cambie, de modo que Ctrl+Z lo revierta.</item>
///   <item>Reconstruye las columnas dinámicas de las dos matrices de factores
///   —una <see cref="DataGridTextColumn"/> por cada <see cref="CasoCarga"/>—
///   ya que <c>DataGrid.Columns</c> no es bindeable y el conjunto de casos es
///   variable. Cada columna se enlaza al factor vía el indexer de
///   <see cref="CombinacionCarga"/>.</item>
/// </list>
/// </summary>
public partial class CargasCombinacionesView : UserControl
{
    /// <summary>Casos con suscripción a <c>PropertyChanged</c> activa.</summary>
    private readonly List<CasoCarga> _casosEnganchados = new();

    /// <summary>Colección de casos suscrita actualmente (o <c>null</c>).</summary>
    private ObservableCollection<CasoCarga>? _casos;

    public CargasCombinacionesView() => InitializeComponent();

    // ---- Ciclo de vida: enganche de la reconstrucción de columnas ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;

        var casos = main.CargasCombinaciones.Casos;
        if (!ReferenceEquals(_casos, casos))
        {
            if (_casos is not null)
                _casos.CollectionChanged -= OnCasosColeccionCambiada;
            _casos = casos;
            _casos.CollectionChanged += OnCasosColeccionCambiada;
        }
        RehookCasos();
        ReconstruirTodasLasColumnas();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_casos is not null)
            _casos.CollectionChanged -= OnCasosColeccionCambiada;
        _casos = null;
        foreach (var c in _casosEnganchados)
            c.PropertyChanged -= OnCasoCambiado;
        _casosEnganchados.Clear();
    }

    private void OnCasosColeccionCambiada(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookCasos();
        ReconstruirTodasLasColumnas();
    }

    /// <summary>
    /// Un cambio del <see cref="CasoCarga.Codigo"/> invalida los encabezados y
    /// los <c>Binding</c> indexados de las columnas de las matrices.
    /// </summary>
    private void OnCasoCambiado(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CasoCarga.Codigo))
            ReconstruirTodasLasColumnas();
    }

    /// <summary>Reengancha la suscripción a <c>PropertyChanged</c> de cada caso.</summary>
    private void RehookCasos()
    {
        foreach (var c in _casosEnganchados)
            c.PropertyChanged -= OnCasoCambiado;
        _casosEnganchados.Clear();
        if (_casos is null) return;
        foreach (var c in _casos)
        {
            c.PropertyChanged += OnCasoCambiado;
            _casosEnganchados.Add(c);
        }
    }

    private void ReconstruirTodasLasColumnas()
    {
        if (_casos is null) return;
        ReconstruirColumnasMatriz(GridMatrizServicio, _casos);
        ReconstruirColumnasMatriz(GridMatrizUltimas, _casos);
    }

    /// <summary>
    /// Reconstruye las columnas de factor de una matriz: conserva la columna
    /// fija «Combinación» (índice 0) y añade una <see cref="DataGridTextColumn"/>
    /// por cada caso, enlazada al factor vía el indexer de
    /// <see cref="CombinacionCarga"/> (ruta <c>[código]</c>).
    /// </summary>
    private static void ReconstruirColumnasMatriz(DataGrid grid, IEnumerable<CasoCarga> casos)
    {
        while (grid.Columns.Count > 1)
            grid.Columns.RemoveAt(grid.Columns.Count - 1);

        foreach (var caso in casos)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header  = caso.Codigo,
                Width   = new DataGridLength(64),
                Binding = new Binding($"[{caso.Codigo}]") { StringFormat = "0.###" },
            });
        }
    }

    // ---- Puente de edición inline → snapshot de Undo ----

    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CargasCombinacionesViewModel vm)
            vm.SnapshotAntesDeEditar();
    }

    // ---- Barra de herramientas ----

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

    /// <summary>
    /// Exportación a <c>.DZP</c> — stub de la Iteración 4. La escritura del
    /// formato DISEST se materializa en una iteración posterior.
    /// </summary>
    private void OnExportarClick(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "La exportación de la base de cargas a un archivo DISEST .DZP " +
            "estará disponible en una próxima iteración.",
            "Exportar .DZP", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// Normalización (re-sembrado por norma) — stub de la Iteración 4. El
    /// re-sembrado con confirmación se materializa en una iteración posterior.
    /// </summary>
    private void OnNormalizarClick(object sender, RoutedEventArgs e)
        => MessageBox.Show(
            "La normalización (re-sembrado de casos y combinaciones según la " +
            "norma seleccionada) estará disponible en una próxima iteración.",
            "Normalizar", MessageBoxButton.OK, MessageBoxImage.Information);
}
