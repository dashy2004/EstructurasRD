using Avalonia.Controls;
using Avalonia.Interactivity;
using LosasPlus.ViewModels;

namespace LosasPlus.Views;

/// <summary>
/// Editor de columnas (Fase J.10): tabla editable de las columnas del primer
/// nivel + botones agregar/eliminar.
/// </summary>
public partial class ColumnasEditorView : UserControl
{
    public ColumnasEditorView()
    {
        InitializeComponent();
    }

    private void OnAgregar(object? sender, RoutedEventArgs e)
        => (DataContext as ColumnasEditorViewModel)?.Agregar();

    private void OnEliminar(object? sender, RoutedEventArgs e)
        => (DataContext as ColumnasEditorViewModel)?.Eliminar();
}
