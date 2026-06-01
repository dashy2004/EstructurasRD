using System.Numerics;
using LosasPlus.Models;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del constructor de escena 3D del edificio (Fase I.2). Verifica el
/// massing esquemático: pisos a su cota, columnas entre niveles y la caja
/// envolvente que usa la cámara para encuadrar.
/// </summary>
public class EscenaEdificioTests
{
    private static Edificio EdificioDosNiveles()
    {
        var ed = new Edificio();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var nivel = new Nivel { Cota = cota };
            var sis = new Sistema();
            sis.Losas.Add(new Losa { Lx = 4, Ly = 4 }); // área 16 → lado 4 → h 2
            nivel.Sistemas.Add(sis);
            ed.Niveles.Add(nivel);
        }
        return ed;
    }

    [Fact]
    public void Edificio_nulo_o_sin_niveles_da_escena_vacia()
    {
        Assert.Empty(EscenaEdificio.Construir(null).Segmentos);
        Assert.Empty(EscenaEdificio.Construir(new Edificio()).Segmentos);
    }

    [Fact]
    public void Dos_niveles_generan_pisos_columnas_y_AABB()
    {
        var esc = EscenaEdificio.Construir(EdificioDosNiveles());

        Assert.Equal(12, esc.Segmentos.Count);             // 8 aristas de piso + 4 columnas
        Assert.Equal(new Vector3(-2, 0, -2), esc.Min);
        Assert.Equal(new Vector3(2, 3, 2), esc.Max);
        Assert.Equal(new Vector3(0, 1.5f, 0), esc.Centro);
    }

    [Fact]
    public void Un_nivel_sin_losas_usa_lado_por_defecto_y_no_tiene_columnas()
    {
        var ed = new Edificio();
        ed.Niveles.Add(new Nivel { Cota = 0 });

        var esc = EscenaEdificio.Construir(ed);
        Assert.Equal(4, esc.Segmentos.Count);              // solo el rectángulo del piso
        Assert.Equal(EscenaEdificio.LadoPorDefecto / 2f, esc.Max.X, 3);
    }

    [Fact]
    public void La_camara_encuadra_la_escena()
    {
        var esc = EscenaEdificio.Construir(EdificioDosNiveles());
        var cam = new CamaraOrbital();
        cam.Encuadrar(esc.Min, esc.Max);

        Assert.True(Vector3.Distance(cam.Objetivo, esc.Centro) < 1e-3f);
        Assert.True(cam.Distancia > 0f);
    }
}
