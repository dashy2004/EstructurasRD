using System;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del dibujo de columnas reales en la escena 3D. Desde K.6 cada
/// <see cref="Columna"/> se dibuja como una <b>caja extruida</b> (prisma
/// rectangular Base×Peralte×Altura = 12 aristas) en su posición de planta, en
/// vez de un único segmento vertical, y extiende la caja envolvente.
/// </summary>
public class EscenaEdificioColumnasTests
{
    [Fact]
    public void Nivel_sin_columnas_no_agrega_segmentos_verticales()
    {
        var ed = new Edificio();
        ed.Niveles.Add(new Nivel { Cota = 0 }); // footprint por defecto → 4 aristas de piso
        Assert.Equal(4, EscenaEdificio.Construir(ed).Segmentos.Count);
    }

    [Fact]
    public void Cada_columna_se_dibuja_como_caja_extruida_en_su_posicion()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        // Sección por defecto 0.30×0.30, Altura 3 → caja centrada en (10,8).
        nivel.Columnas.Add(new Columna { Nombre = "C-1", CoordenadaX = 10, CoordenadaY = 8, Altura = 3 });
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);
        Assert.Equal(16, esc.Segmentos.Count); // 4 piso + 12 caja

        var caja = esc.Segmentos.Skip(4).ToList();
        Assert.Equal(12, caja.Count);
        // Caja 0.30×0.30 (half 0.15) centrada en (10,8), extruida de y=0 a y=3.
        Assert.Equal(9.85, caja.Min(s => MathF.Min(s.A.X, s.B.X)), 3);
        Assert.Equal(10.15, caja.Max(s => MathF.Max(s.A.X, s.B.X)), 3);
        Assert.Equal(7.85, caja.Min(s => MathF.Min(s.A.Z, s.B.Z)), 3);
        Assert.Equal(8.15, caja.Max(s => MathF.Max(s.A.Z, s.B.Z)), 3);
        Assert.Equal(0.00, caja.Min(s => MathF.Min(s.A.Y, s.B.Y)), 3);
        Assert.Equal(3.00, caja.Max(s => MathF.Max(s.A.Y, s.B.Y)), 3);
    }

    [Fact]
    public void Las_columnas_extienden_la_caja_envolvente()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        nivel.Columnas.Add(new Columna { CoordenadaX = 10, CoordenadaY = 8, Altura = 3 });
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);
        Assert.True(esc.Max.X >= 10);
        Assert.Equal(3, esc.Max.Y, 4);
        Assert.True(esc.Max.Z >= 8);
    }
}
