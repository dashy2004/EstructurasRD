using Avalonia.Controls;
using Avalonia.Input;
using MemoriaPlus.ViewModels;
using Avalonia.Markup.Xaml;
using Shared.UI.ViewModels;

namespace MemoriaPlus.Views;

public partial class ExploradorView : UserControl
{
    public ExploradorView() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Doble-click en una fila: abre el proyecto y cambia a Cálculos. Port a
    /// Avalonia: WPF MouseDoubleClick → DoubleTapped.
    /// </summary>
    private void ProyectosGrid_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.ProyectoRecienteSeleccionado is null) return;
        if (vm.AbrirEnCalculosCommand.CanExecute(null))
            vm.AbrirEnCalculosCommand.Execute(null);
    }
}
