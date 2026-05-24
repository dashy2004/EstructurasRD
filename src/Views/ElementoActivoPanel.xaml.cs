using System.Windows.Controls;

namespace LosasPlus.Views;

/// <summary>
/// Panel persistente del elemento estructural activo (Liga B paso B2 del
/// Plan Maestro). Sin lógica de code-behind: el binding tree consume
/// exclusivamente <c>MainViewModel.ElementoActivo</c> de tipo
/// <c>ElementoActivoViewModel</c> (adapter read-only inmutable). Las
/// transiciones placeholder ↔ "elemento activo" se gobiernan por
/// <c>DataTrigger</c>s sobre la propiedad <c>EsVacio</c>.
/// </summary>
public partial class ElementoActivoPanel : UserControl
{
    public ElementoActivoPanel()
    {
        InitializeComponent();
    }
}
