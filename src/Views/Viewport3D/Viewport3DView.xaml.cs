using System.Windows.Controls;

namespace LosasPlus.Views.Viewport3D;

/// <summary>
/// Code-behind del visor 3D (Fase 3D-I1 del Plan Maestro de Expansión 3D).
/// Sólo inicializa el XAML; toda la lógica reside en
/// <see cref="LosasPlus.ViewModels.Viewport3D.Viewport3DViewModel"/>, que se
/// inyecta vía <c>DataContext={Binding Viewport3D}</c> desde el shell.
/// </summary>
public partial class Viewport3DView : UserControl
{
    public Viewport3DView()
    {
        InitializeComponent();
    }
}
