using LosasPlus.Models;
using LosasPlus.Persistence;
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
    public void El_origen_geografico_cae_exactamente_en_el_origen_local()
    {
        var (x, y) = Origen().ALocal(LatOrigen, LonOrigen);

        Assert.Equal(0.0, x, 6);
        Assert.Equal(0.0, y, 6);
    }

    [Fact]
    public void Sin_rotacion_avanzar_al_norte_es_avanzar_en_y_local()
    {
        // El espejo del test de AGeografico: subir la latitud el equivalente a
        // 100 m debe caer en (0, 100) del plano local.
        var (x, y) = Origen().ALocal(LatOrigen + 100.0 / MetrosPorGrado, LonOrigen);

        Assert.Equal(0.0, x, 6);
        Assert.Equal(100.0, y, 6);
    }

    [Fact]
    public void Con_rotacion_de_90_grados_avanzar_al_este_es_avanzar_en_y_local()
    {
        // Azimut 90°: el +Y local apunta al Este, así que avanzar al Este en el
        // mapa debe caer sobre el eje +Y del plano.
        var geo = Origen();
        geo.RotacionNorte = 90.0;

        double lonEste = LonOrigen
            + 100.0 / (MetrosPorGrado * System.Math.Cos(LatOrigen * System.Math.PI / 180.0));

        var (x, y) = geo.ALocal(LatOrigen, lonEste);

        Assert.Equal(0.0, x, 6);
        Assert.Equal(100.0, y, 6);
    }

    [Fact]
    public void ALocal_es_la_inversa_exacta_de_AGeografico()
    {
        // Ida y vuelta con rotación arbitraria: el punto debe volver a casa.
        // Es la garantía que necesita la fase N — un reporte de IncidenciasRD
        // proyectado a la planta y devuelto al mapa no puede derivar.
        var geo = Origen();
        geo.RotacionNorte = 37.5;

        var (lat, lon) = geo.AGeografico(123.45, -67.89);
        var (x, y) = geo.ALocal(lat, lon);

        Assert.Equal(123.45, x, 6);
        Assert.Equal(-67.89, y, 6);
    }

    [Fact]
    public void Por_defecto_el_proyecto_no_esta_georreferenciado()
    {
        // Georreferenciar es opcional: los proyectos existentes (.lpx.json v1/v2)
        // deben seguir cargando sin coordenadas geográficas.
        Assert.Null(new Proyecto().Georreferencia);
    }

    [Fact]
    public void La_georreferencia_sobrevive_el_roundtrip_de_serializacion()
    {
        var proyecto = new Proyecto
        {
            Georreferencia = new Georreferencia
            {
                Latitud = LatOrigen,
                Longitud = LonOrigen,
                Elevacion = 25.0,
                RotacionNorte = 15.0,
            },
        };

        var clon = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(proyecto));

        Assert.NotNull(clon.Georreferencia);
        Assert.Equal(LatOrigen, clon.Georreferencia!.Latitud, 9);
        Assert.Equal(LonOrigen, clon.Georreferencia.Longitud, 9);
        Assert.Equal(25.0, clon.Georreferencia.Elevacion, 9);
        Assert.Equal(15.0, clon.Georreferencia.RotacionNorte, 9);
        Assert.Equal(4326, clon.Georreferencia.Epsg);
    }

    [Fact]
    public void Un_proyecto_sin_georreferencia_serializa_sin_la_clave()
    {
        // WhenWritingNull: los .lpx.json de proyectos no ubicados quedan
        // byte-idénticos a los de antes de la K.6 — cero ruido en diffs.
        Assert.DoesNotContain("georreferencia", ProyectoSerializer.ToJson(new Proyecto()));
    }
}
