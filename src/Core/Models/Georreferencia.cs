using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Models;

/// <summary>
/// Ancla el sistema de coordenadas <b>local</b> de la planta (metros, origen
/// arbitrario elegido por el proyectista) a coordenadas <b>geográficas WGS84</b>
/// — la pieza que le faltaba al modelo para saber <i>dónde</i> está en el mundo.
///
/// <para>
/// Es el cimiento de la Fase K.6 y, con ella, de las fases M (mapa 3D urbano,
/// CityGML / 3D&#160;Tiles) y N (integración con IncidenciasRD): en cuanto el
/// edificio tiene lat/lon, puede cruzarse con los reportes georreferenciados de
/// IncidenciasRD y con las nubes de puntos de VisionRD, que ya hablan GeoJSON.
/// </para>
///
/// <para>
/// <b>Modelo de proyección — plano tangente local.</b> A la escala de un
/// edificio o una parcela (cientos de metros) la curvatura terrestre es
/// despreciable: el error frente a una proyección rigurosa es milimétrico.
/// Cuando la Fase L (obras de arte: puentes, alineaciones de kilómetros) lo
/// exija, se sustituye por UTM (EPSG:32619, zona 19N para RD) detrás de esta
/// misma interfaz, sin tocar a los consumidores.
/// </para>
///
/// <para>
/// Tipo <b>puro de dominio</b> — sin dependencias de UI, testeable headless
/// (mismo patrón que <c>SafExporter</c> e <c>IfcExporter</c>).
/// </para>
/// </summary>
public sealed class Georreferencia : INotifyPropertyChanged
{
    /// <summary>Metros por grado de latitud en el elipsoide WGS84 (valor medio).</summary>
    public const double MetrosPorGrado = 111_320.0;

    private double _latitud;
    private double _longitud;
    private double _elevacion;
    private double _rotacionNorte;
    private int _epsg = 4326;

    /// <summary>Latitud del origen local, en grados decimales WGS84 (+N).</summary>
    public double Latitud
    {
        get => _latitud;
        set { _latitud = value; OnPropertyChanged(); }
    }

    /// <summary>Longitud del origen local, en grados decimales WGS84 (+E).</summary>
    public double Longitud
    {
        get => _longitud;
        set { _longitud = value; OnPropertyChanged(); }
    }

    /// <summary>Elevación del origen local sobre el nivel medio del mar, en metros.</summary>
    public double Elevacion
    {
        get => _elevacion;
        set { _elevacion = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Azimut del eje <b>+Y local</b> respecto al Norte verdadero, en grados y
    /// positivo en sentido <b>horario</b> (convención topográfica). Con 0° el
    /// eje +Y apunta al Norte y el +X al Este; con 90° el +Y apunta al Este.
    /// Es la magnitud que consume <c>IfcMapConversion.TrueNorth</c>.
    /// </summary>
    public double RotacionNorte
    {
        get => _rotacionNorte;
        set { _rotacionNorte = value; OnPropertyChanged(); }
    }

    /// <summary>Código EPSG del sistema de referencia de salida (4326 = WGS84 lat/lon).</summary>
    public int Epsg
    {
        get => _epsg;
        set { _epsg = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Convierte un punto del plano local (metros) a coordenadas geográficas.
    /// </summary>
    /// <param name="xLocal">Abscisa local en metros, medida desde el origen.</param>
    /// <param name="yLocal">Ordenada local en metros, medida desde el origen.</param>
    /// <returns>Latitud y longitud en grados decimales WGS84.</returns>
    public (double Latitud, double Longitud) AGeografico(double xLocal, double yLocal)
    {
        double azimut = RotacionNorte * Math.PI / 180.0;

        // Con azimut 0 el +Y local es el Norte y el +X el Este; el azimut gira
        // ambos ejes en sentido horario, así que +X queda 90° horario de +Y.
        double metrosEste = xLocal * Math.Cos(azimut) + yLocal * Math.Sin(azimut);
        double metrosNorte = -xLocal * Math.Sin(azimut) + yLocal * Math.Cos(azimut);

        double latitud = Latitud + metrosNorte / MetrosPorGrado;
        double longitud = Longitud
            + metrosEste / (MetrosPorGrado * Math.Cos(Latitud * Math.PI / 180.0));

        return (latitud, longitud);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class Proyecto
{
    private Georreferencia? _georreferencia;

    /// <summary>
    /// Georreferenciación del proyecto, o <c>null</c> si no se ha ubicado en el
    /// mapa. Es <b>opcional</b> a propósito: los proyectos <c>.lpx.json</c> v1 y
    /// v2 existentes deben seguir cargando sin coordenadas. Contraparte
    /// estructurada del campo de texto libre <see cref="Ubicacion"/>.
    /// </summary>
    public Georreferencia? Georreferencia
    {
        get => _georreferencia;
        set { _georreferencia = value; OnPropertyChanged(); }
    }
}
