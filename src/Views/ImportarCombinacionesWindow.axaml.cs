using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LosasPlus.Views;

/// <summary>
/// Modal «Importar combinaciones desde DISEST». DataContext: un
/// ImportadorCombinacionesViewModel; el cierre lo maneja el callback Cerrar.
/// </summary>
public partial class ImportarCombinacionesWindow : Window
{
    public ImportarCombinacionesWindow() => AvaloniaXamlLoader.Load(this);
}
