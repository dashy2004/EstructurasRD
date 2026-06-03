using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Shared.UI.Views;

/// <summary>
/// Sub-vista «Preferencias» del modo Configuración (H4): formato numérico (decimales,
/// cultura), sistema de unidades y auto-backup. DataContext heredado del
/// <see cref="ViewModels.ConfiguracionViewModel"/>; sin lógica propia.
/// </summary>
public partial class PreferenciasView : UserControl
{
    public PreferenciasView() => AvaloniaXamlLoader.Load(this);
}
