using System;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Render3D;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del dibujo de vigas reales en la escena 3D (K.6). Una <see cref="Viga"/>
/// con sección (Base×Peralte de su primer tramo) se dibuja como una <b>caja
/// extruida</b> a lo largo de su eje en planta (prisma recto = 12 aristas), en
/// vez de un único segmento.
/// </summary>
public class EscenaEdificioVigasTests
{
    [Fact]
    public void Viga_real_se_dibuja_como_caja_extruida_de_su_seccion()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var viga = new Viga { OrigenX = 2, OrigenY = 3, AnguloGrados = 0 };
        viga.Tramos.Add(new TramoViga { Longitud = 4, Base = 0.30, Peralte = 0.50 });
        nivel.Vigas.Add(viga);
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);

        // Sólo el prisma de la viga (12 aristas), sin rectángulo de piso.
        Assert.Equal(12, esc.Segmentos.Count);

        var caja = esc.Segmentos.ToList();
        Assert.Equal(12, caja.Count);

        // Eje +X de (2,3) a (6,3) a cota 0; sección 0.30 (ancho, eje Z) × 0.50
        // (peralte, eje Y), centrada en el eje. Caja: x∈[2,6], z∈[2.85,3.15], y∈[-0.25,0.25].
        Assert.Equal(2.00, caja.Min(s => MathF.Min(s.A.X, s.B.X)), 3);
        Assert.Equal(6.00, caja.Max(s => MathF.Max(s.A.X, s.B.X)), 3);
        Assert.Equal(2.85, caja.Min(s => MathF.Min(s.A.Z, s.B.Z)), 3);
        Assert.Equal(3.15, caja.Max(s => MathF.Max(s.A.Z, s.B.Z)), 3);
        Assert.Equal(-0.25, caja.Min(s => MathF.Min(s.A.Y, s.B.Y)), 3);
        Assert.Equal(0.25, caja.Max(s => MathF.Max(s.A.Y, s.B.Y)), 3);
    }

    [Fact]
    public void Viga_sin_tramos_dibuja_solo_su_eje()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        nivel.Vigas.Add(new Viga { OrigenX = 0, OrigenY = 0, AnguloGrados = 0 }); // sin tramos → sin sección
        ed.Niveles.Add(nivel);

        // Sólo 1 segmento de eje (viga sin sección/longitud), sin rectángulo de piso.
        var esc = EscenaEdificio.Construir(ed);
        Assert.Equal(1, esc.Segmentos.Count);
    }
}
