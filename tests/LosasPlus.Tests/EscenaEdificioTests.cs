using System;
using System.Linq;
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

        Assert.Equal(20, esc.Segmentos.Count);             // 8 aristas de piso + 8 aristas de losas reales + 4 columnas
        Assert.Equal(new Vector3(-2, 0, -2), esc.Min);
        Assert.Equal(new Vector3(4, 3, 4), esc.Max);
        Assert.Equal(new Vector3(1, 1.5f, 1), esc.Centro);
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

    /// <summary>
    /// K.6 / bug 3D: una columna real (con sección Base×Peralte y Altura) debe
    /// dibujarse como una <b>caja extruida</b> (prisma rectangular = 12 aristas),
    /// no como un único segmento vertical. La caja se centra en (CoordenadaX,
    /// CoordenadaY) en planta y va de la cota del nivel a cota + Altura.
    /// </summary>
    [Fact]
    public void Columna_real_se_dibuja_como_caja_extruida_de_su_seccion()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        nivel.Columnas.Add(new Columna
        {
            CoordenadaX = 5, CoordenadaY = 7,
            Base = 0.40, Peralte = 0.30, Altura = 3.0,
        });
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);

        // 4 aristas del piso (sin losas → lado por defecto) + 12 de la caja.
        Assert.Equal(16, esc.Segmentos.Count);

        // La caja es todo menos el rectángulo de piso (que se añade primero).
        var caja = esc.Segmentos.Skip(4).ToList();
        Assert.Equal(12, caja.Count);

        // Sección Base(0.40, eje X) × Peralte(0.30, eje Z) centrada en (5,7),
        // extruida de y=0 a y=3.
        float minX = caja.Min(s => MathF.Min(s.A.X, s.B.X));
        float maxX = caja.Max(s => MathF.Max(s.A.X, s.B.X));
        float minZ = caja.Min(s => MathF.Min(s.A.Z, s.B.Z));
        float maxZ = caja.Max(s => MathF.Max(s.A.Z, s.B.Z));
        float minY = caja.Min(s => MathF.Min(s.A.Y, s.B.Y));
        float maxY = caja.Max(s => MathF.Max(s.A.Y, s.B.Y));

        Assert.Equal(4.80, minX, 3);
        Assert.Equal(5.20, maxX, 3);
        Assert.Equal(6.85, minZ, 3);
        Assert.Equal(7.15, maxZ, 3);
        Assert.Equal(0.00, minY, 3);
        Assert.Equal(3.00, maxY, 3);

        // Exactamente 4 aristas verticales (esquinas) de y=0 a y=3.
        int verticales = caja.Count(s =>
            MathF.Abs(s.A.Y - 0f) < 1e-4f && MathF.Abs(s.B.Y - 3f) < 1e-4f);
        Assert.Equal(4, verticales);
    }
}
