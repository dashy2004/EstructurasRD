using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class BusquedaView : UserControl
{
    public BusquedaView() => InitializeComponent();

    /// <summary>
    /// Auto-focus en el TextBox cuando la vista se hace visible — el usuario
    /// puede empezar a tipear inmediatamente después de presionar Ctrl+F o
    /// clickear el modo Búsqueda en la sidebar.
    /// </summary>
    private void SearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && (bool)e.NewValue)
        {
            tb.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Doble-click en una card de resultado: dispara <see cref="BusquedaViewModel.IrAResultadoCommand"/>
    /// con el item seleccionado. Funciona para las 3 secciones (proyectos /
    /// sistemas / losas).
    ///
    /// <para>
    /// Resuelve el <see cref="BusquedaViewModel"/> en 2 saltos para soportar
    /// los dos modos de uso:
    /// </para>
    /// <list type="bullet">
    ///   <item>Cuando DataContext = BusquedaViewModel directo (LosasPlus.App
    ///         setea el ContentControl.DataContext al sub-VM).</item>
    ///   <item>Cuando DataContext expone una property <c>Busqueda</c> via
    ///         <see cref="IBusquedaHost"/> (MemoriaPlus.App, donde el
    ///         MainViewModel agrupa varios sub-VMs).</item>
    /// </list>
    /// </summary>
    private void Resultados_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not BusquedaResultado resultado) return;
        var vm = ResolverBusquedaVM();
        if (vm is null) return;
        if (vm.IrAResultadoCommand.CanExecute(resultado))
            vm.IrAResultadoCommand.Execute(resultado);
    }

    private BusquedaViewModel? ResolverBusquedaVM() => DataContext switch
    {
        BusquedaViewModel direct => direct,
        IBusquedaHost host       => host.Busqueda,
        _                        => null,
    };
}
