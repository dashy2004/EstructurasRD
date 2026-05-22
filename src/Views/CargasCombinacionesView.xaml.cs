using System.Windows;
using System.Windows.Controls;
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
}
