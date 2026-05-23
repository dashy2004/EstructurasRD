using System.Windows.Controls;
using LosasPlus.ViewModels.Auditoria;

namespace LosasPlus.Views.Auditoria;

/// <summary>
/// Code-behind del dashboard de auditoría global (Fase 7, Iteración 2).
/// La vista es de sólo lectura: el único handler aquí es el botón
/// "✕ Todos" que limpia el <c>FiltroGravedad</c> del sub-VM
/// — el ComboBox de severidad no expone una opción "null" propia.
/// </summary>
public partial class AuditoriaView : UserControl
{
    public AuditoriaView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Resetea el filtro de severidad. Bindeado al botón de la toolbar.
    /// El DataContext del Grid raíz es <see cref="AuditoriaViewModel"/>.
    /// </summary>
    private void OnLimpiarFiltro(object sender, System.Windows.RoutedEventArgs e)
    {
        // El DataContext del UserControl es MainViewModel; el del Grid raíz
        // es Auditoria (sub-VM). Resolvemos por el FrameworkElement.Tag o,
        // más simple, navegando al sub-VM desde el botón vía VisualTree.
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is AuditoriaViewModel vm)
        {
            vm.FiltroGravedad = null;
        }
    }
}
