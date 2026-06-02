using Avalonia.Controls;
using LosasPlus.ViewModels.Vigas;

namespace LosasPlus.Views.Vigas;

/// <summary>
/// Editor de vigas continuas. Port a Avalonia: <c>FrameworkElement</c>→<c>Control</c>,
/// InitializeComponent generado; el evento DataGrid.BeginningEdit toma un snapshot
/// para deshacer antes de que el usuario edite una celda.
/// </summary>
public partial class VigaEditorView : UserControl
{
    public VigaEditorView() => InitializeComponent();

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if ((sender as Control)?.DataContext is VigaEditorViewModel vm)
            vm.SnapshotAntesDeEditar();
    }
}
