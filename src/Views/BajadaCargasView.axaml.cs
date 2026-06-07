using Avalonia.Controls;
using Avalonia.Interactivity;
using LosasPlus.ViewModels;
using MemoriaPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Vista «Bajada de cargas» (Fase J.4): tabla de carga acumulada por nivel +
/// predimensionado de zapata. El botón «Recalcular» relee el edificio activo.
/// </summary>
public partial class BajadaCargasView : UserControl
{
    public BajadaCargasView()
    {
        InitializeComponent();
    }

    private void OnRecalcular(object? sender, RoutedEventArgs e)
        => (DataContext as BajadaCargasViewModel)?.Recalcular();

    private async void OnExportarXlsx(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BajadaCargasViewModel vm) return;

        var path = await AppServices.Dialogs.SaveFileAsync(
            "Exportar bajada de cargas", "bajada_de_cargas.xlsx", ".xlsx",
            new FileFilter("Excel Workbook", new[] { "*.xlsx" }));
        if (path is null) return;

        vm.ExportarXlsx(path);
    }

    private void OnPredimensionarZapatas(object? sender, RoutedEventArgs e)
        => (DataContext as BajadaCargasViewModel)?.PredimensionarZapatas();

    private async void OnExportarCsvClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ZapataDisenoRow row)
        {
            var path = await AppServices.Dialogs.SaveFileAsync(
                "Exportar diseño de zapata", $"zapata_{row.NombreColumna}.csv", ".csv",
                new FileFilter("CSV (Delimitado por comas)", new[] { "*.csv" }));
            
            if (path is not null)
            {
                LosasPlus.Services.ZapataDisenoExporter.ExportCsv(row.Diseno, path);
            }
        }
    }
}
