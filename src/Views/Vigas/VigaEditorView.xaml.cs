using System.Windows;
using System.Windows.Controls;
using LosasPlus.ViewModels.Vigas;
using LosasPlus.Views.Rc;

namespace LosasPlus.Views.Vigas;

/// <summary>
/// Vista del editor de vigas continuas (Fase 3, Iteración 2). El code-behind
/// sólo puentea la edición inline de los <see cref="DataGrid"/> al snapshot de
/// Undo del ViewModel: <c>BeginningEdit</c> captura el estado <b>antes</b> de
/// que la celda cambie, de modo que Ctrl+Z lo revierta. Toda la reactividad y
/// el renderizado de los diagramas viven en <see cref="VigaEditorViewModel"/>.
/// </summary>
public partial class VigaEditorView : UserControl
{
    public VigaEditorView() => InitializeComponent();

    private void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is VigaEditorViewModel vm)
            vm.SnapshotAntesDeEditar();
    }

    /// <summary>
    /// Abre la ventana modal de diseño de sección RC para el tramo seleccionado.
    /// </summary>
    private void OnDisenarSeccionClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not VigaEditorViewModel vm) return;
        var ventana = new RcSectionDesignWindow
        {
            DataContext = vm.DisenoSeccion,
            Owner       = Window.GetWindow(this),
        };
        ventana.ShowDialog();
    }
}
