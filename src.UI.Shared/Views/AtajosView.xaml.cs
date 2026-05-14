using System.Windows;
using System.Windows.Controls;
using LosasPlus.Persistence;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class AtajosView : UserControl
{
    public AtajosView() => InitializeComponent();

    /// <summary>
    /// Click en un botón con apariencia de keycap: abre <see cref="KeyCaptureWindow"/>
    /// modal, espera el resultado, y actualiza el AtajosConfig del VM con el
    /// nuevo gesture. El binding {Binding Atajos.X} refresca la UI sola
    /// gracias a INPC en AtajosConfig.
    /// </summary>
    private void EditableKeyCap_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string atajoId) return;
        if (DataContext is not ConfiguracionViewModel vm) return;

        var etiqueta = AtajoIds.Etiqueta(atajoId);
        var gestureActual = vm.Atajos.Get(atajoId);

        var dlg = new KeyCaptureWindow(etiqueta, gestureActual)
        {
            Owner = Window.GetWindow(this),
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.GestureCapturado))
        {
            vm.Atajos.Set(atajoId, dlg.GestureCapturado);
        }
    }
}
