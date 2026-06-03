using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LosasPlus.Cargas;
using LosasPlus.ViewModels;
using Shared.UI.Services;

namespace LosasPlus.Views;

/// <summary>
/// Vista «Cargas y Combinaciones». El code-behind reconstruye las columnas
/// dinámicas de las matrices (una por caso) y puentea la edición inline al Undo.
/// Port a Avalonia: OpenFileDialog/MessageBox→AppServices; ShowDialog async;
/// FrameworkElement→Control; System.Windows.Data.Binding→Avalonia.Data.Binding.
/// </summary>
public partial class CargasCombinacionesView : UserControl
{
    private readonly List<CasoCarga> _casosEnganchados = new();
    private ObservableCollection<CasoCarga>? _casos;

    public CargasCombinacionesView() => InitializeComponent();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        var casos = main.CargasCombinaciones.Casos;
        if (!ReferenceEquals(_casos, casos))
        {
            if (_casos is not null) _casos.CollectionChanged -= OnCasosColeccionCambiada;
            _casos = casos;
            _casos.CollectionChanged += OnCasosColeccionCambiada;
        }
        RehookCasos();
        ReconstruirTodasLasColumnas();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_casos is not null) _casos.CollectionChanged -= OnCasosColeccionCambiada;
        _casos = null;
        foreach (var c in _casosEnganchados) c.PropertyChanged -= OnCasoCambiado;
        _casosEnganchados.Clear();
    }

    private void OnCasosColeccionCambiada(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RehookCasos();
        ReconstruirTodasLasColumnas();
    }

    private void OnCasoCambiado(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CasoCarga.Codigo)) ReconstruirTodasLasColumnas();
    }

    private void RehookCasos()
    {
        foreach (var c in _casosEnganchados) c.PropertyChanged -= OnCasoCambiado;
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

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if ((sender as Control)?.DataContext is CargasCombinacionesViewModel vm)
            vm.SnapshotAntesDeEditar();
    }

    private async void OnImportarClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main) return;
        var vm = main.CargasCombinaciones;

        var ruta = await AppServices.Dialogs.OpenFileAsync("Importar combinaciones DISEST",
            new FileFilter("Combinaciones DISEST", new[] { "*.dzp", "*.cez" }),
            new FileFilter("Todos los archivos", new[] { "*.*" }));
        if (ruta is null) return;

        try
        {
            var texto = File.ReadAllText(ruta, Encoding.Latin1);
            var formato = ruta.EndsWith(".cez", StringComparison.OrdinalIgnoreCase)
                ? FormatoCombinaciones.Cez
                : FormatoCombinaciones.Dzp;

            var importador = vm.CrearImportador(texto, formato, Path.GetFileName(ruta));
            var win = new ImportarCombinacionesWindow { DataContext = importador };
            importador.Cerrar = win.Close;
            if (this.GetVisualRoot() is Window owner)
                await win.ShowDialog(owner);
            else
                win.Show();

            if (importador.Aplicado) vm.NotificarRestauracion();
        }
        catch (CombinacionesParseException ex)
        {
            await AppServices.MessageBox.InfoAsync("Importar combinaciones",
                "No se pudo leer el archivo de combinaciones:\n\n" + ex.Message);
        }
        catch (IOException ex)
        {
            await AppServices.MessageBox.InfoAsync("Importar combinaciones",
                "No se pudo abrir el archivo:\n\n" + ex.Message);
        }
    }

    private void OnExportarClick(object? sender, RoutedEventArgs e)
        => _ = AppServices.MessageBox.InfoAsync("Exportar .DZP",
            "La exportación de la base de cargas a un archivo DISEST .DZP estará disponible en una próxima iteración.");

    private void OnNormalizarClick(object? sender, RoutedEventArgs e)
        => _ = AppServices.MessageBox.InfoAsync("Normalizar",
            "La normalización (re-sembrado de casos y combinaciones según la norma seleccionada) estará disponible en una próxima iteración.");
}
