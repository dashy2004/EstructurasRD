using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LosasPlus.Views;

/// <summary>
/// Editor unificado (UI1): hospeda Planta 2D, el único lienzo desde UI1.6.
/// Conserva el punto de extensión del shell y el accessor de captura para el export.
/// </summary>
public partial class EditorUnificadoView : UserControl
{
    public EditorUnificadoView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Editor de planta hosteado — lo usa la captura del export a Excel (UI1.6).</summary>
    public Planta2DEditorView? Editor => this.FindControl<Planta2DEditorView>("EditorView");
}
