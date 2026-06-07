using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    /// <summary>Longitud del tramo, en metros.</summary>
    public double Longitud
    {
        get => _longitud;
        set { _longitud = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Ancho de la sección rectangular, en metros. Al cambiarlo se recalcula la
    /// <see cref="Inercia"/> bruta <c>Base·Peralte³/12</c> (D1: acoplar la rigidez
    /// a la geometría — el motor lee <see cref="Inercia"/>, así que editar la
    /// sección debe regenerar los diagramas M/V/δ).
    /// </summary>
    public double Base
    {
        get => _base;
        set { _base = value; OnPropertyChanged(); RecalcularInercia(); }
    }

    /// <summary>
    /// Peralte (altura) de la sección rectangular, en metros. Al cambiarlo se
    /// recalcula la <see cref="Inercia"/> bruta <c>Base·Peralte³/12</c> (D1).
    /// </summary>
    public double Peralte
    {
        get => _peralte;
        set { _peralte = value; OnPropertyChanged(); RecalcularInercia(); }
    }

    /// <summary>
    /// Momento de inercia de la sección respecto al eje de flexión, en m⁴.
    /// Para una sección rectangular bruta es <c>Base·Peralte³/12</c>; cualquier
    /// edición de <see cref="Base"/> o <see cref="Peralte"/> lo recalcula a ese
    /// valor (D1: la rigidez a flexión <c>E·I</c> sigue a la geometría). El setter
    /// admite además fijar un valor explícito (p. ej. sección no rectangular o
    /// agrietada), pero volverá a sincronizarse en la próxima edición de la sección.
    /// En la UI la columna «I» es de sólo lectura: es un valor derivado.
    /// </summary>
    public double Inercia
    {
        get => _inercia;
        set { _inercia = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Recalcula <see cref="Inercia"/> = <c>Base·Peralte³/12</c> (m⁴) a partir de
    /// la geometría rectangular bruta. Lo invocan los setters de <see cref="Base"/>
    /// y <see cref="Peralte"/> para mantener la rigidez acoplada a la sección.
    /// </summary>
    private void RecalcularInercia()
        => Inercia = _base * _peralte * _peralte * _peralte / 12.0;

    /// <summary>Módulo de elasticidad del material E, en kN/m².</summary>
    public double ModuloElasticidad
    {
        get => _moduloElasticidad;
        set { _moduloElasticidad = value; OnPropertyChanged(); }
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
