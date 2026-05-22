using System.Windows;
using System.Windows.Controls;
using LosasPlus.ViewModels.Columnas;

namespace LosasPlus.Views.Columnas;

/// <summary>
/// Vista del editor de columnas RC (Fase 5, Iteración 2). El code-behind sólo
/// despacha eventos de edición al método unificado
/// <see cref="ColumnaEditorViewModel.SnapshotAntesDeEditar"/>, que toma un
/// snapshot de Undo ANTES de que el valor cambie:
/// <list type="bullet">
/// <item><c>DataGrid.BeginningEdit</c> en la grilla de capas.</item>
/// <item><c>TextBox.GotFocus</c> en los campos numéricos de parámetros.</item>
/// </list>
/// Así Ctrl+Z revierte tanto la edición de una celda como la edición de un
/// campo numérico de la sección.
/// </summary>
public partial class ColumnaEditorView : UserControl
{
    public ColumnaEditorView() => InitializeComponent();

    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is ColumnaEditorViewModel vm) vm.SnapshotAntesDeEditar();
    }

    private void OnSnapshotAntesDeEditar(object sender, RoutedEventArgs e)
    {
        if (DataContext is ColumnaEditorViewModel vm) vm.SnapshotAntesDeEditar();
    }
}
