using System.Windows;
using System.Windows.Controls;
using LosasPlus.ViewModels.Zapatas;

namespace LosasPlus.Views.Zapatas;

/// <summary>
/// Vista del editor de zapatas aisladas (Fase 6, Iteración 2). El code-behind
/// sólo despacha eventos de edición al método unificado
/// <see cref="ZapataEditorViewModel.SnapshotAntesDeEditar"/>, que toma un
/// snapshot de Undo ANTES de que el valor cambie:
/// <list type="bullet">
/// <item><c>DataGrid.BeginningEdit</c> en la grilla de cargas.</item>
/// <item><c>TextBox.GotFocus</c> en los campos numéricos de Geometría y
/// Terreno.</item>
/// </list>
/// Así Ctrl+Z revierte tanto la edición de una celda como la edición de un
/// campo numérico.
/// </summary>
public partial class ZapataEditorView : UserControl
{
    public ZapataEditorView() => InitializeComponent();

    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is ZapataEditorViewModel vm) vm.SnapshotAntesDeEditar();
    }

    private void OnSnapshotAntesDeEditar(object sender, RoutedEventArgs e)
    {
        if (DataContext is ZapataEditorViewModel vm) vm.SnapshotAntesDeEditar();
    }
}
