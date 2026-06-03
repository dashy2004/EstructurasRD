using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using LosasPlus.Persistence;
using Shared.UI.ViewModels;

namespace Shared.UI.Views;

public partial class AtajosView : UserControl
{
    public AtajosView() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Click en un keycap: abre <see cref="KeyCaptureWindow"/> modal, espera el
    /// resultado, y actualiza el AtajosConfig del VM. El binding {Binding
    /// Atajos.X} refresca solo gracias a INPC.
    ///
    /// <para>Port a Avalonia: WPF usaba ShowDialog()==true + Owner; aquí
    /// <c>await ShowDialog&lt;string?&gt;(owner)</c>.</para>
    /// </summary>
    private async void EditableKeyCap_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not string atajoId) return;
        if (DataContext is not ConfiguracionViewModel vm) return;
        if (this.GetVisualRoot() is not Window owner) return;

        var etiqueta = AtajoIds.Etiqueta(atajoId);
        var gestureActual = vm.Atajos.Get(atajoId);

        var dlg = new KeyCaptureWindow(etiqueta, gestureActual);
        var resultado = await dlg.ShowDialog<string?>(owner);
        if (!string.IsNullOrEmpty(resultado))
            vm.Atajos.Set(atajoId, resultado);
    }
}
