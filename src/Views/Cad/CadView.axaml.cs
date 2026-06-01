using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Stub de Fase E del editor visual CAD. El control real (render retenido
/// WPF → render inmediato de Avalonia) se porta en la última fase; este
/// placeholder existe para que el MainWindow real compile y arranque.
/// </summary>
public partial class CadView : UserControl
{
    public CadView() => AvaloniaXamlLoader.Load(this);
}
