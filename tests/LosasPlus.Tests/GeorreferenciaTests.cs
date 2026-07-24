using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la georreferenciación del proyecto (Fase K.6): anclar el sistema de
/// coordenadas local de la planta (metros, origen arbitrario) a coordenadas
/// geográficas WGS84, para que el modelo estructural pueda vivir en el mapa 3D
/// urbano y cruzarse con los reportes georreferenciados de IncidenciasRD.
///
/// <para>
/// Escenario de referencia: Santo Domingo (Av. Winston Churchill), el mismo
/// entorno que usa la ingesta de demo de VisionRD.
/// </para>
/// </summary>
public class GeorreferenciaTests
{
    // Metros por grado de latitud en el elipsoide WGS84 (valor medio).
    private const double MetrosPorGrado = 111_320.0;

    private const double LatOrigen = 18.4700;   // Santo Domingo
    private const double LonOrigen = -69.9400;

    private static Georreferencia Origen() => new()
    {
        Latitud = LatOrigen,
        Longitud = LonOrigen,
        Elevacion = 25.0,
        RotacionNorte = 0.0,
    };

    [Fact]
    public void El_origen_local_cae_exactamente_en_las_coordenadas_del_origen()
    {
        var (lat, lon) = Origen().AGeografico(0.0, 0.0);

        Assert.Equal(LatOrigen, lat, 9);
        Assert.Equal(LonOrigen, lon, 9);
    }

    [Fact]
    public void Sin_rotacion_el_eje_y_local_apunta_al_norte()
    {
        // 100 m en +Y  →  sube la latitud, la longitud no se mueve.
        var (lat, lon) = Origen().AGeografico(0.0, 100.0);

        Assert.Equal(LatOrigen + 100.0 / MetrosPorGrado, lat, 9);
        Assert.Equal(LonOrigen, lon, 9);
    }

    [Fact]
    public void Sin_rotacion_el_eje_x_local_apunta_al_este_con_convergencia_de_meridianos()
    {
        // 100 m en +X  →  la longitud avanza MÁS que un grado-equivalente en
        // latitud, porque los meridianos convergen: hay que dividir por cos(lat).
        var (lat, lon) = Origen().AGeografico(100.0, 0.0);

        double esperado = LonOrigen
            + 100.0 / (MetrosPorGrado * System.Math.Cos(LatOrigen * System.Math.PI / 180.0));

        Assert.Equal(LatOrigen, lat, 9);
        Assert.Equal(esperado, lon, 9);
    }

    [Fact]
    public void Una_rotacion_de_90_grados_hace_que_el_eje_y_local_apunte_al_este()
    {
        // Azimut del eje +Y = 90° (horario desde el Norte) → +Y local es el Este.
        var geo = Origen();
        geo.RotacionNorte = 90.0;

        var (lat, lon) = geo.AGeografico(0.0, 100.0);

        double esperado = LonOrigen
            + 100.0 / (MetrosPorGrado * System.Math.Cos(LatOrigen * System.Math.PI / 180.0));

        Assert.Equal(LatOrigen, lat, 9);
        Assert.Equal(esperado, lon, 9);
    }

    [Fact]
    public void Una_rotacion_de_90_grados_hace_que_el_eje_x_local_apunte_al_sur()
    {
        // Si +Y va al Este, el sistema (dextrógiro en planta) manda +X al Sur.
        var geo = Origen();
        geo.RotacionNorte = 90.0;

        var (lat, lon) = geo.AGeografico(100.0, 0.0);

        Assert.Equal(LatOrigen - 100.0 / MetrosPorGrado, lat, 9);
        Assert.Equal(LonOrigen, lon, 9);
    }

    [Fact]
    public void Por_defecto_el_proyecto_no_esta_georreferenciado()
    {
        // Georreferenciar es opcional: los proyectos existentes (.lpx.json v1/v2)
        // deben seguir cargando sin coordenadas geográficas.
        Assert.Null(new Proyecto().Georreferencia);
    }
}
