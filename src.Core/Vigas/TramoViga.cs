using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LosasPlus.Rc;

namespace LosasPlus.Vigas;

/// <summary>
/// Un tramo de una <see cref="Viga"/>: el segmento de sección y material
/// constantes entre dos puntos consecutivos. Aporta su rigidez a flexión
/// <c>E·I</c> al motor de resolución — varios tramos con distinta
/// <see cref="Inercia"/> modelan una viga de inercia variable.
///
/// <para>
/// Unidades (base SI kN-m): <see cref="Longitud"/>, <see cref="Base"/> y
/// <see cref="Peralte"/> en metros; <see cref="Inercia"/> en m⁴;
/// <see cref="ModuloElasticidad"/> en kN/m².
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class TramoViga : INotifyPropertyChanged
{
    private double _longitud = 5.0;             // m
    private double _base = 0.30;                // m
    private double _peralte = 0.50;             // m
    private double _inercia = 0.003125;         // m⁴  (0.30·0.50³/12)
    private double _moduloElasticidad = 2.5e7;  // kN/m²  (≈ hormigón f'c 28 MPa)
    private SeccionRC _seccion = new();
    private RefuerzoLongitudinal _refuerzo = new();

    /// <summary>Longitud del tramo, en metros.</summary>
    public double Longitud
    {
        get => _longitud;
        set { _longitud = value; OnPropertyChanged(); }
    }

    /// <summary>Ancho de la sección rectangular, en metros.</summary>
    public double Base
    {
        get => _base;
        set { _base = value; OnPropertyChanged(); }
    }

    /// <summary>Peralte (altura) de la sección rectangular, en metros.</summary>
    public double Peralte
    {
        get => _peralte;
        set { _peralte = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Momento de inercia de la sección respecto al eje de flexión, en m⁴.
    /// Se almacena de forma independiente para admitir secciones no
    /// rectangulares o agrietadas; para una sección rectangular bruta es
    /// <c>Base·Peralte³/12</c>.
    /// </summary>
    public double Inercia
    {
        get => _inercia;
        set { _inercia = value; OnPropertyChanged(); }
    }

    /// <summary>Módulo de elasticidad del material E, en kN/m².</summary>
    public double ModuloElasticidad
    {
        get => _moduloElasticidad;
        set { _moduloElasticidad = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Sección de concreto reforzado del tramo para el diseño RC (Fase 4) —
    /// geometría y materiales que evalúa <c>RcDesignEngine</c> a flexión.
    /// </summary>
    public SeccionRC Seccion
    {
        get => _seccion;
        set { _seccion = value; OnPropertyChanged(); }
    }

    /// <summary>Acero de refuerzo longitudinal del tramo (Fase 4).</summary>
    public RefuerzoLongitudinal Refuerzo
    {
        get => _refuerzo;
        set { _refuerzo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Cargas aplicadas sobre este tramo. Una carga
    /// <see cref="TipoCargaElemento.Distribuida"/> actúa uniforme sobre toda
    /// la <see cref="Longitud"/> del tramo. Colección get-only con
    /// inicializador.
    /// </summary>
    public ObservableCollection<CargaElemento> Cargas { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
