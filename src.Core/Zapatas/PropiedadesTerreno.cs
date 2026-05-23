using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Zapatas;

/// <summary>
/// Propiedades geotécnicas del terreno de fundación bajo una
/// <see cref="ZapataAislada"/> (Fase 6 de la suite estructural).
///
/// <para>
/// La <see cref="PresionAdmisible"/> se introduce a mano —es el valor
/// neto admisible del estudio de mecánica de suelos, ya incluido cualquier
/// factor de seguridad— y el motor la usa como límite superior contra el
/// que se evalúa la presión de contacto máxima de cada combinación de
/// servicio. El <see cref="PesoEspecificoSuelo"/> se usa para computar el
/// peso del suelo de relleno sobre la zapata (entre <c>EspesorH</c> y
/// <c>ProfundidadDesplante</c>), que se suma a la carga axial permanente.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class PropiedadesTerreno : INotifyPropertyChanged
{
    private double _presionAdmisible = 150.0;       // kN/m² — neto admisible
    private double _pesoEspecificoSuelo = 18.0;     // kN/m³ — natural

    /// <summary>
    /// Presión neta admisible del terreno, en kN/m². Es el valor del estudio
    /// de suelos ya minorado por el factor de seguridad; el motor lo usa
    /// directamente como límite contra <c>q_max</c>.
    /// </summary>
    public double PresionAdmisible
    {
        get => _presionAdmisible;
        set { _presionAdmisible = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Peso específico natural del suelo de relleno sobre la zapata, en
    /// kN/m³ (típicamente 16–20 kN/m³ para suelos no saturados).
    /// </summary>
    public double PesoEspecificoSuelo
    {
        get => _pesoEspecificoSuelo;
        set { _pesoEspecificoSuelo = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
