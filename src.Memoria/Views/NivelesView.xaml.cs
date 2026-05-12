using System.Windows;
using System.Windows.Controls;
using LosasPlus.Models;

namespace MemoriaPlus.Views;

public partial class NivelesView : UserControl
{
    public NivelesView() => InitializeComponent();

    /// <summary>
    /// Click en la celda TIPO del DataGrid: abre el selector visual de tipo
    /// de losa pre-cargado con el tipo actual. Al confirmar el modal, escribe
    /// el nuevo código en la Losa de la fila (lo que dispara recálculo en
    /// vivo + re-validación normativa via los handlers de MainViewModel).
    /// </summary>
    private void AbrirSelectorTipoLosa_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not Losa losa) return;

        var dlg = new SelectorTipoLosaWindow(losa.Tipo)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() == true && dlg.TipoConfirmado is int nuevoTipo)
        {
            losa.Tipo = nuevoTipo;
        }
    }
}
