using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class ExploradorView : UserControl
{
    public ExploradorView() => InitializeComponent();

    /// <summary>
    /// Doble-click en una fila del DataGrid: abre el proyecto y cambia a la
    /// pestaña Cálculos. Si el click cae sobre el header de la tabla (sin
    /// fila seleccionada), no hace nada.
    /// </summary>
    private void ProyectosGrid_OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.ProyectoRecienteSeleccionado is null) return;
        if (vm.AbrirEnCalculosCommand.CanExecute(null))
            vm.AbrirEnCalculosCommand.Execute(null);
    }
}
