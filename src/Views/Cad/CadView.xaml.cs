using System.Windows.Controls;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Vista del modo <b>Plano CAD</b> (Fase 1.B). Aloja el
/// <see cref="CadCanvasHost"/> y la barra de importación de planos DXF.
/// El <c>DataContext</c> es el <c>MainViewModel</c> — de ahí salen
/// <c>CadEditor</c> (sub-VM del CAD) y <c>Sistema</c> (sistema activo).
/// </summary>
public partial class CadView : UserControl
{
    public CadView()
    {
        InitializeComponent();
    }
}
