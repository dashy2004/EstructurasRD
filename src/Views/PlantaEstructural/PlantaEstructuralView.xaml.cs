using System.Windows.Controls;

namespace LosasPlus.Views.PlantaEstructural;

/// <summary>
/// View del modo Planta Estructural (Liga C paso C1). Sin lógica de
/// code-behind: toda la interacción está en el canvas custom
/// (<see cref="PlantaEstructuralCanvas"/>) y todo el state en el
/// <c>PlantaEstructuralViewModel</c> que MainViewModel expone como
/// sub-VM <c>PlantaEstructural</c>.
/// </summary>
public partial class PlantaEstructuralView : UserControl
{
    public PlantaEstructuralView()
    {
        InitializeComponent();
    }
}
