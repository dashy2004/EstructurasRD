using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Columnas;

/// <summary>
/// Un punto de demanda actuante sobre una <see cref="Columna"/> — el par
/// (Pu, Mu) que resulta de una combinación de carga, contrastado contra el
/// diagrama de interacción P-M de diseño para evaluar la conformidad
/// (Fase 5, Iteración 3).
///
/// <para>
/// El <see cref="ColumnaDesignEngine"/> verifica cada demanda contra la
/// frontera <c>CurvaDiseno</c> mediante un rayo radial desde el origen y
/// devuelve el ratio de eficiencia <c>D/C</c>.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class DemandaColumna : INotifyPropertyChanged
{
    private string _nombreCombo = "";
    private double _pu;   // kN — positivo = compresión
    private double _mu;   // kN·m

    /// <summary>
    /// Etiqueta de la combinación de carga que origina esta demanda (p. ej.
    /// «1.2D + 1.6L», «0.9D + 1.0E»). Sólo para identificación visual; el
    /// motor sólo consume <see cref="Pu"/> y <see cref="Mu"/>.
    /// </summary>
    public string NombreCombo
    {
        get => _nombreCombo;
        set { _nombreCombo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Fuerza axial última actuante Pu, en kN. Positiva = compresión,
    /// consistente con la convención del diagrama de interacción.
    /// </summary>
    public double Pu
    {
        get => _pu;
        set { _pu = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Momento flector último actuante Mu, en kN·m. El signo es relevante:
    /// el diagrama de interacción es asimétrico en columnas con refuerzo
    /// asimétrico, así que un Mu negativo se verifica contra el ramal
    /// izquierdo y uno positivo contra el derecho.
    /// </summary>
    public double Mu
    {
        get => _mu;
        set { _mu = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
