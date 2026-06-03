using System;
using System.Linq;
using System.Numerics;
using LosasPlus.Models;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del constructor de escena 3D del edificio (Fase I.2). Verifica el
/// massing esquemático: pisos a su cota, losas/columnas como volúmenes extruidos
/// (K.6) y la caja envolvente que usa la cámara para encuadrar.
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
            sis.Losas.Add(new Losa { Lx = 4, Ly = 4 }); // área 16 → lado 4 → h 2; espesor default 0.12
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
    public void Dos_niveles_generan_pisos_losas_extruidas_columnas_y_AABB()
    {
        var esc = EscenaEdificio.Construir(EdificioDosNiveles());

        // Sólo elementos reales: 12 aristas de la caja de losa por nivel × 2 = 24
        // (ya no hay rectángulo de piso ni tronco entre niveles).
        Assert.Equal(24, esc.Segmentos.Count);

        // AABB de las losas reales: paño en [0,4]×[0,4]; espesor 0.12 hacia abajo.
        Assert.Equal(0.0, esc.Min.X, 3);
        Assert.Equal(-0.12, esc.Min.Y, 3);
        Assert.Equal(0.0, esc.Min.Z, 3);
        Assert.Equal(4.0, esc.Max.X, 3);
        Assert.Equal(3.0, esc.Max.Y, 3);
        Assert.Equal(4.0, esc.Max.Z, 3);
        Assert.Equal(2.0, esc.Centro.X, 3);
        Assert.Equal(1.44, esc.Centro.Y, 3);
        Assert.Equal(2.0, esc.Centro.Z, 3);
    }

    [Fact]
    public void Un_nivel_sin_elementos_no_genera_massing()
    {
        var ed = new Edificio();
        ed.Niveles.Add(new Nivel { Cota = 0 });

        // Ya no se dibuja el rectángulo esquemático de piso → nivel vacío = sin segmentos.
        Assert.Empty(EscenaEdificio.Construir(ed).Segmentos);
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
    /// K.6 / bug 3D: una losa real se dibuja como una <b>caja delgada extruida</b>
    /// por su <see cref="Losa.Espesor"/> (prisma = 12 aristas), con el tope a la
    /// cota del nivel y el fondo a cota − espesor, en vez de un rectángulo plano.
    /// </summary>
    [Fact]
    public void Losa_se_dibuja_como_caja_extruida_por_su_espesor()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var sis = new Sistema();
        sis.Losas.Add(new Losa { Lx = 4, Ly = 3, CoordenadaX = 1, CoordenadaY = 2, Espesor = 0.20 });
        nivel.Sistemas.Add(sis);
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);

        // Sólo la caja de la losa (12 aristas), sin rectángulo de piso.
        Assert.Equal(12, esc.Segmentos.Count);

        var caja = esc.Segmentos.ToList();
        Assert.Equal(12, caja.Count);

        // Paño (1,2)-(5,5) en planta; tope y=0, fondo y=-0.20.
        Assert.Equal(1.0, caja.Min(s => MathF.Min(s.A.X, s.B.X)), 3);
        Assert.Equal(5.0, caja.Max(s => MathF.Max(s.A.X, s.B.X)), 3);
        Assert.Equal(2.0, caja.Min(s => MathF.Min(s.A.Z, s.B.Z)), 3);
        Assert.Equal(5.0, caja.Max(s => MathF.Max(s.A.Z, s.B.Z)), 3);
        Assert.Equal(-0.20, caja.Min(s => MathF.Min(s.A.Y, s.B.Y)), 3);
        Assert.Equal(0.00, caja.Max(s => MathF.Max(s.A.Y, s.B.Y)), 3);
    }

    /// <summary>
    /// K.6 / bug 3D: una columna real (sección Base×Peralte, Altura) se dibuja
    /// como caja extruida (12 aristas), centrada en planta, de la cota a cota+Altura.
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

        // Sólo la caja de la columna (12 aristas), sin rectángulo de piso.
        Assert.Equal(12, esc.Segmentos.Count);

        var caja = esc.Segmentos.ToList();
        Assert.Equal(12, caja.Count);

        Assert.Equal(4.80, caja.Min(s => MathF.Min(s.A.X, s.B.X)), 3);
        Assert.Equal(5.20, caja.Max(s => MathF.Max(s.A.X, s.B.X)), 3);
        Assert.Equal(6.85, caja.Min(s => MathF.Min(s.A.Z, s.B.Z)), 3);
        Assert.Equal(7.15, caja.Max(s => MathF.Max(s.A.Z, s.B.Z)), 3);
        Assert.Equal(0.00, caja.Min(s => MathF.Min(s.A.Y, s.B.Y)), 3);
        Assert.Equal(3.00, caja.Max(s => MathF.Max(s.A.Y, s.B.Y)), 3);

        // Exactamente 4 aristas verticales (esquinas) de y=0 a y=3.
        int verticales = caja.Count(s =>
            MathF.Abs(s.A.Y - 0f) < 1e-4f && MathF.Abs(s.B.Y - 3f) < 1e-4f);
        Assert.Equal(4, verticales);
    }

    [Fact]
    public void Losas_se_dibujan_a_la_elevacion_de_su_sistema()
    {
        // Un nivel (cota 0) con dos sistemas: uno a elevación 0 y otro a 3 m.
        // La losa del sistema elevado debe quedar a y=3 (no solaparse con la otra).
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var sBajo = new Sistema { Elevacion = 0 };
        sBajo.Losas.Add(new Losa { CoordenadaX = 0, CoordenadaY = 0, Lx = 4, Ly = 4, Espesor = 0.12 });
        var sAlto = new Sistema { Elevacion = 3 };
        sAlto.Losas.Add(new Losa { CoordenadaX = 0, CoordenadaY = 0, Lx = 4, Ly = 4, Espesor = 0.12 });
        nivel.Sistemas.Add(sBajo);
        nivel.Sistemas.Add(sAlto);
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);

        // El tope de la losa del sistema elevado llega a y = cota + elevación = 3.
        Assert.Equal(3.0, esc.Max.Y, 3);
    }
}
