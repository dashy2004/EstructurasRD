using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using LosasPlus.Models;

namespace MemoriaPlus.Views;

public partial class NivelesView : UserControl
{
    public NivelesView() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Click en la celda TIPO: abre el selector visual modal y, si el usuario
    /// confirma, actualiza el Tipo de la losa. Port a Avalonia: WPF ShowDialog()
    /// + GetWindow → await ShowDialog&lt;int?&gt;(owner).
    /// </summary>
    private async void AbrirSelectorTipoLosa_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not Losa losa) return;
        if (this.GetVisualRoot() is not Window owner) return;

        var dlg = new SelectorTipoLosaWindow(losa.Tipo);
        var nuevo = await dlg.ShowDialog<int?>(owner);
        if (nuevo is int t) losa.Tipo = t;
    }
}
