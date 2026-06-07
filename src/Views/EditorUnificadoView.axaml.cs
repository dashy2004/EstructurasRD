using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LosasPlus.Views;

/// <summary>
/// Editor unificado (Paso 5): hospeda en un solo lugar la edición estructural
/// (Planta 2D, base) y la herramienta CAD/import (DXF·PDF·muros), como dos
/// sub-pestañas que comparten el mismo <c>MainViewModel</c>. Primer paso de la
/// unificación — los canvas internos no cambian. Ver
/// <c>docs/plan-unificar-cad-planta2d.md</c>.
/// </summary>
public partial class EditorUnificadoView : UserControl
{
    public EditorUnificadoView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
