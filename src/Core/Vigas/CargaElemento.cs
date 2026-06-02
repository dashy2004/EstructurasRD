using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Vigas;

/// <summary>Naturaleza de una <see cref="CargaElemento"/> sobre un tramo de viga.</summary>
public enum TipoCargaElemento
{
    /// <summary>Carga puntual aplicada en una <see cref="CargaElemento.Posicion"/> concreta.</summary>
    Puntual,

    /// <summary>Carga uniformemente distribuida sobre toda la longitud del tramo.</summary>
    Distribuida,
}

/// <summary>
/// Una carga aplicada sobre un <see cref="TramoViga"/>. El
/// <see cref="CodigoCaso"/> la vincula a un caso de carga (<c>CasoCarga</c>) de
/// la base de cargas del proyecto (Fase 2): el motor de resolución la mayora
/// por el factor que cada combinación asigne a ese caso.
///
/// <para>
/// Unidades: <see cref="Magnitud"/> en kN si <see cref="Tipo"/> es
/// <see cref="TipoCargaElemento.Puntual"/>, o en kN/m si es
/// <see cref="TipoCargaElemento.Distribuida"/>. <see cref="Posicion"/> en
/// metros, relativa al inicio del tramo (sólo aplica a cargas puntuales).
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class CargaElemento : INotifyPropertyChanged
{
    private TipoCargaElemento _tipo = TipoCargaElemento.Distribuida;
    private double _magnitud;
    private double _posicion;
    private string _codigoCaso = "";

    /// <summary>Constructor sin parámetros — requerido por la deserialización JSON.</summary>
    public CargaElemento() { }

    /// <summary>Constructor de conveniencia para construir cargas en código.</summary>
    public CargaElemento(TipoCargaElemento tipo, double magnitud, string codigoCaso, double posicion = 0.0)
    {
        _tipo = tipo;
        _magnitud = magnitud;
        _codigoCaso = codigoCaso;
        _posicion = posicion;
    }

    /// <summary>Puntual o distribuida — ver <see cref="TipoCargaElemento"/>.</summary>
    public TipoCargaElemento Tipo
    {
        get => _tipo;
        set { _tipo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Magnitud de la carga: kN para una carga puntual, kN/m para una
    /// distribuida. Un valor positivo representa una carga descendente
    /// (gravitatoria).
    /// </summary>
    public double Magnitud
    {
        get => _magnitud;
        set { _magnitud = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Posición de una carga puntual, en metros relativos al inicio del
    /// tramo. No tiene efecto sobre las cargas distribuidas (que actúan
    /// uniformes sobre todo el tramo).
    /// </summary>
    public double Posicion
    {
        get => _posicion;
        set { _posicion = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Código del caso de carga (<c>CasoCarga.Codigo</c>) al que pertenece la
    /// carga — debe coincidir con un caso de la base de cargas del proyecto.
    /// Determina con qué factor la mayora cada combinación.
    /// </summary>
    public string CodigoCaso
    {
        get => _codigoCaso;
        set { _codigoCaso = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
