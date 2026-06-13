using LosasPlus.Models.Cad;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del servicio puro de geometría de redimensionado de muros (UI1.10).
/// </summary>
public class MurosPlantaServiceTests
{
    private static Muro M(double x0, double y0, double x1, double y1)
        => new Muro { Id = 1, PuntoInicio = new PuntoCad(x0, y0), PuntoFin = new PuntoCad(x1, y1) };

    [Fact]
    public void AsaExtremo_cerca_de_inicio_devuelve_0()
    {
        var m = M(0, 0, 5, 0);
        Assert.Equal(0, MurosPlantaService.AsaExtremo(m, new PuntoM(0.05, 0.02), 0.2));
    }

    [Fact]
    public void AsaExtremo_cerca_de_fin_devuelve_1()
    {
        var m = M(0, 0, 5, 0);
        Assert.Equal(1, MurosPlantaService.AsaExtremo(m, new PuntoM(4.95, 0.0), 0.2));
    }

    [Fact]
    public void AsaExtremo_lejos_devuelve_null()
    {
        var m = M(0, 0, 5, 0);
        Assert.Null(MurosPlantaService.AsaExtremo(m, new PuntoM(2.5, 1.0), 0.2));
    }

    [Fact]
    public void AsaExtremo_empate_gana_el_mas_cercano()
    {
        // Muro corto: ambos extremos dentro de tol; el punto está más cerca de fin.
        var m = M(0, 0, 0.1, 0);
        Assert.Equal(1, MurosPlantaService.AsaExtremo(m, new PuntoM(0.08, 0.0), 0.2));
    }

    [Fact]
    public void MoverExtremoLibre_punto_normal_es_identidad()
    {
        var fijo = new PuntoCad(0, 0);
        var r = MurosPlantaService.MoverExtremoLibre(fijo, new PuntoM(3, 4), 0.10);
        Assert.Equal(3.0, r.X, 9);
        Assert.Equal(4.0, r.Y, 9);
    }

    [Fact]
    public void MoverExtremoLibre_clampa_a_longitud_minima()
    {
        var fijo = new PuntoCad(0, 0);
        // cursor a 0.04 m sobre +X; minLen 0.10 ⇒ se empuja a (0.10, 0).
        var r = MurosPlantaService.MoverExtremoLibre(fijo, new PuntoM(0.04, 0.0), 0.10);
        Assert.Equal(0.10, r.X, 9);
        Assert.Equal(0.0, r.Y, 9);
    }

    [Fact]
    public void ProyectarSobreEje_punto_sobre_el_eje_es_identidad()
    {
        var fijo = new PuntoCad(0, 0);
        var refEje = new PuntoCad(5, 0);            // eje horizontal +X
        var r = MurosPlantaService.ProyectarSobreEje(fijo, refEje, new PuntoM(3, 0), 0.10);
        Assert.Equal(3.0, r.X, 9);
        Assert.Equal(0.0, r.Y, 9);
    }

    [Fact]
    public void ProyectarSobreEje_punto_fuera_del_eje_proyecta_perpendicular()
    {
        var fijo = new PuntoCad(0, 0);
        var refEje = new PuntoCad(5, 0);            // eje horizontal
        // cursor (3, 2) ⇒ proyección sobre el eje = (3, 0).
        var r = MurosPlantaService.ProyectarSobreEje(fijo, refEje, new PuntoM(3, 2), 0.10);
        Assert.Equal(3.0, r.X, 9);
        Assert.Equal(0.0, r.Y, 9);
    }

    [Fact]
    public void ProyectarSobreEje_diagonal_mantiene_la_orientacion()
    {
        var fijo = new PuntoCad(0, 0);
        var refEje = new PuntoCad(3, 4);            // eje a 5 m, dir (0.6, 0.8)
        // cursor en (6, 8) está sobre el eje a 10 m ⇒ identidad.
        var r = MurosPlantaService.ProyectarSobreEje(fijo, refEje, new PuntoM(6, 8), 0.10);
        Assert.Equal(6.0, r.X, 9);
        Assert.Equal(8.0, r.Y, 9);
    }

    [Fact]
    public void ProyectarSobreEje_no_voltea_y_clampa_a_longitud_minima()
    {
        var fijo = new PuntoCad(0, 0);
        var refEje = new PuntoCad(5, 0);            // eje +X
        // cursor en el lado opuesto (-3, 0): t < 0 ⇒ clamp a minLen sobre +X.
        var r = MurosPlantaService.ProyectarSobreEje(fijo, refEje, new PuntoM(-3, 0), 0.10);
        Assert.Equal(0.10, r.X, 9);
        Assert.Equal(0.0, r.Y, 9);
    }

    [Fact]
    public void ProyectarSobreEje_eje_degenerado_cae_a_libre()
    {
        var fijo = new PuntoCad(2, 2);
        var refEje = new PuntoCad(2, 2);            // longitud ~0: sin dirección
        var r = MurosPlantaService.ProyectarSobreEje(fijo, refEje, new PuntoM(5, 6), 0.10);
        Assert.Equal(5.0, r.X, 9);
        Assert.Equal(6.0, r.Y, 9);
    }
}
